using System.Text.Json;
using System.Threading.Channels;

namespace CodexAutoResume.Core;

// All commands, events and polls pass through one gate. The RPC reader never waits
// for that gate, so a request can complete while its notifications are queued.
public sealed class Scheduler : IAsyncDisposable
{
    private readonly ICodexClient client;
    private readonly IStateStore store;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<string, CancellationToken, Task<string>> repositoryKey;
    private readonly Func<bool> ownershipStillValid;
    private readonly bool backgroundPolling;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Channel<CodexEvent> events = Channel.CreateUnbounded<CodexEvent>();
    private readonly CancellationTokenSource lifetime = new();
    private readonly AppState state;
    private Task? eventTask, pollTask;
    private volatile bool connected;
    private bool ownershipConfirmed, storageFault;
    private DateTimeOffset nextPoll;
    private UsageSnapshot? usage;
    private DateTimeOffset usageCheckedAt;
    public event Action<AppState>? Changed;
    public event Action<string>? Notice;
    public bool IsConnected => connected;
    public AppState Snapshot => StateJson.Clone(state); // Read on the gate or before starting.

    public Scheduler(ICodexClient client, IStateStore store, Func<DateTimeOffset>? clock = null,
        Func<string, CancellationToken, Task<string>>? resolveRepository = null, Func<bool>? ownershipCheck = null,
        bool backgroundPolling = true)
    {
        this.client = client; this.store = store; now = clock ?? (() => DateTimeOffset.UtcNow);
        repositoryKey = resolveRepository ?? WorkspaceIdentity.GetAsync;
        ownershipStillValid = ownershipCheck ?? (() => true);
        this.backgroundPolling = backgroundPolling;
        state = store.Load();
        state.Enabled = false; // A new process must explicitly reacquire exclusive ownership.
        foreach (var c in state.Conversations) c.Revalidated = false;
        client.EventReceived += e =>
        {
            if (e.RequestId != null || e.Method is "turn/started" or "turn/completed" or "thread/status/changed" or "error" or "account/rateLimits/updated")
                events.Writer.TryWrite(e);
        };
        client.Disconnected += reason => events.Writer.TryWrite(new("connection/lost", JsonSerializer.SerializeToElement(new { reason })));
    }

    public async Task ConnectAsync(string executable, bool exclusiveOwnership, CancellationToken ct = default)
    {
        await InGateAsync(async () =>
        {
            if (!exclusiveOwnership || !ownershipStillValid()) throw new InvalidOperationException("Close Codex Desktop and other clients, then explicitly confirm exclusive ownership.");
            await client.ConnectAsync(executable, ct);
            state.CodexPath = executable;
            connected = ownershipConfirmed = true;
            Log("Connected to owned Codex app-server. Desktop UI synchronization is unsupported.");
            await RevalidateAllAsync(ct);
            Commit();
        }, ct);
        eventTask ??= ProcessEventsAsync();
        if (backgroundPolling) pollTask ??= PollAsync();
    }

    public async Task<IReadOnlyList<ThreadSnapshot>> DiscoverAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ThreadSnapshot> threads = [];
        await InGateAsync(async () => { RequireConnection(); threads = await client.ListAsync(ct); }, ct);
        return threads;
    }

    public async Task<ThreadSnapshot> InspectAsync(string id, CancellationToken ct = default)
    {
        ThreadSnapshot? result = null;
        await InGateAsync(async () => { RequireConnection(); result = await client.ReadAsync(id, ct); AppServerClient.ValidateId(id, result.Id); }, ct);
        return result!;
    }

    public Task AddAsync(string id, CancellationToken ct = default) => InGateAsync(async () =>
    {
        RequireConnection();
        if (!Guid.TryParse(id, out var uuid)) throw new ArgumentException("Enter an exact session UUID.");
        id = uuid.ToString();
        if (state.Conversations.Count >= 5) throw new InvalidOperationException("The queue supports at most five conversations.");
        if (state.Conversations.Any(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("This conversation is already selected.");
        var thread = await client.ReadAsync(id, ct);
        AppServerClient.ValidateId(id, thread.Id);
        var key = await repositoryKey(thread.Workspace, ct);
        var c = new Conversation { Id = id, Name = thread.Name, Workspace = thread.Workspace, RepositoryKey = key };
        state.Conversations.Add(c);
        ApplyThread(c, thread);
        // Imported interrupted work is eligible; a completed or empty session requires explicit rearm.
        if (thread.LastTurn?.Status == "interrupted" && !c.Attention)
        {
            c.Armed = true; c.ArmedAfterTurnId = thread.LastTurn.Id;
        }
        UpdatePresentation(); Log($"Added {c.Name} ({c.Id})."); Commit();
    }, ct);

    public Task SetEnabledAsync(bool enabled, CancellationToken ct = default) => InGateAsync(() =>
    {
        if (enabled) { RequireConnection(); if (!ownershipStillValid()) throw new InvalidOperationException("Another Codex client is running. Close it before enabling."); }
        state.Enabled = enabled;
        Log(enabled ? "Automatic scheduling ON." : "Automatic scheduling OFF. Active turns continue.");
        UpdatePresentation(); Commit(); return Task.CompletedTask;
    }, ct);

    public Task UpdateAsync(string id, Action<Conversation> update, CancellationToken ct = default) => InGateAsync(() =>
    {
        var c = Find(id);
        var wasParallel = c.Parallel;
        update(c);
        if (c.ExecutionActive && wasParallel != c.Parallel)
        {
            c.Parallel = wasParallel;
            throw new InvalidOperationException("Change the execution lane after the active turn finishes.");
        }
        UpdatePresentation(); Commit(); return Task.CompletedTask;
    }, ct);

    public Task MoveAsync(string id, int offset, CancellationToken ct = default) => InGateAsync(() =>
    {
        var c = Find(id); var from = state.Conversations.IndexOf(c); var to = Math.Clamp(from + offset, 0, state.Conversations.Count - 1);
        state.Conversations.RemoveAt(from); state.Conversations.Insert(to, c); Commit(); return Task.CompletedTask;
    }, ct);

    public Task RemoveAsync(string id, CancellationToken ct = default) => InGateAsync(() =>
    {
        var c = Find(id);
        if (c.ExecutionActive || Uncertain(c)) throw new InvalidOperationException("Cannot remove active or uncertain work. Reconcile it first.");
        state.Conversations.Remove(c); Log($"Removed {c.Name}."); Commit(); return Task.CompletedTask;
    }, ct);

    // Explicit user action. Never invoked by a timer, completion or reconnect.
    public Task RearmAsync(string id, bool acknowledgeUncertain, CancellationToken ct = default) => InGateAsync(async () =>
    {
        RequireConnection();
        var c = Find(id);
        var thread = await client.ReadAsync(c.Id, ct);
        ValidateWorkspace(c, thread);
        if (thread.RuntimeStatus is not ("idle" or "notLoaded") || thread.LastTurn?.Status == "inProgress")
            throw new InvalidOperationException("Codex still reports active or unresolved work. Resolve it in a supported Codex client before retrying.");
        if (Uncertain(c))
        {
            if (!acknowledgeUncertain) throw new InvalidOperationException("Delivery is uncertain. Inspect the conversation before explicitly acknowledging and rearming it.");
            Attempt(c)!.Phase = AttemptPhase.ResolvedManually;
            Log($"User reconciled uncertain attempt for {c.Name}; explicitly rearmed.");
        }
        c.Attention = false; c.ExecutionActive = false; c.Paused = false; c.Armed = true;
        c.AttemptId = null; c.ArmedAfterTurnId = thread.LastTurn?.Id; c.LastTurnId = thread.LastTurn?.Id;
        c.CompletedAt = null; c.CompletionTimeEstimated = false; c.Revalidated = true;
        c.Status = c.ResetAt != null || c.Status == ConversationStatus.RateLimited ? ConversationStatus.RateLimited : ConversationStatus.Queued;
        c.Reason = "Explicitly rearmed by user";
        UpdatePresentation(); Commit();
    }, ct);

    public Task SaveSettingsAsync(bool startWithWindows, CancellationToken ct = default) => InGateAsync(() =>
    {
        state.StartWithWindows = startWithWindows; Commit(); return Task.CompletedTask;
    }, ct);

    public async Task<AppState> GetSnapshotAsync(CancellationToken ct = default)
    {
        AppState? snapshot = null;
        await InGateAsync(() => { snapshot = StateJson.Clone(state); return Task.CompletedTask; }, ct);
        return snapshot!;
    }

    public Task TickAsync(CancellationToken ct = default) => InGateAsync(async () =>
    {
        if (!connected || storageFault) return;
        if (!ownershipStillValid())
        {
            state.Enabled = false; ownershipConfirmed = false;
            foreach (var c in state.Conversations.Where(c => c.Armed && !c.ExecutionActive)) Attention(c, "Another Codex client appeared; exclusive ownership lost. Restart AutoResume after closing other clients.");
            Log("Exclusive ownership lost; future scheduling stopped. Active turns were not interrupted."); Commit(); return;
        }
        if (now() >= nextPoll)
        {
            await RevalidateAllAsync(ct);
            nextPoll = now().AddSeconds(30);
        }
        if (state.Enabled && ownershipConfirmed)
        {
            // Re-read backend permission at reset/backoff boundaries. A countdown reaching
            // zero never grants permission, nor does a low usedPercent or earned credit.
            var waiting = state.Conversations.Where(c => c.Armed && !c.Paused && !c.Attention && !c.ExecutionActive).ToList();
            var hold = state.Conversations.Where(c => c.WaitingForUsage && c.NextCheckAt > now()).OrderByDescending(c => c.NextCheckAt).FirstOrDefault();
            if (hold != null)
            {
                foreach (var c in waiting.Where(c => !c.WaitingForUsage))
                {
                    Limit(c, hold.ResetAt, "Account usage is waiting for a fresh availability check");
                    c.NextCheckAt = hold.NextCheckAt;
                }
            }
            var due = waiting.Where(c => !c.WaitingForUsage || c.NextCheckAt == null || c.NextCheckAt <= now()).ToList();
            if (hold == null && due.Count > 0 && (usage == null || usageCheckedAt.AddSeconds(30) <= now() || due.Any(c => c.WaitingForUsage)))
            {
                try { usage = await client.ReadUsageAsync(ct); usageCheckedAt = now(); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    foreach (var c in due) Attention(c, "Cannot verify usage availability: " + ex.Message);
                }
                foreach (var c in due.Where(c => !c.Attention))
                {
                    if (usage?.CanResume == true)
                    {
                        c.WaitingForUsage = false; c.ResetAt = c.NextCheckAt = null; c.BackoffMinutes = 15; c.Status = ConversationStatus.Queued;
                    }
                    else if (usage != null) Limit(c, usage.ResetAt, usage.Detail, extendBackoff: true);
                }
            }
            foreach (var c in state.Conversations.Where(c => c.Armed && !c.Paused && !c.Attention && !c.ExecutionActive && !c.WaitingForUsage).ToList())
            {
                if (!CanStart(c)) continue;
                await StartAsync(c, ct);
                // A server-side limit on one start must stop subsequent starts this cycle.
                if (c.Status == ConversationStatus.RateLimited || !state.Enabled) break;
            }
        }
        UpdatePresentation(); Commit();
    }, ct);

    private bool CanStart(Conversation c)
    {
        if (!c.Revalidated || Uncertain(c) || usage?.CanResume != true) return false;
        var active = state.Conversations.Where(x => x.ExecutionActive || Uncertain(x)).ToList();
        if (!c.Parallel && active.Any(x => !x.Parallel)) { c.Reason = "Waiting for the sequential lane"; return false; }
        if (active.Any(x => PathsOverlap(c, x))) { c.Reason = "Waiting for conflicting repository/workspace work"; return false; }
        return true;
    }

    private static bool PathsOverlap(Conversation a, Conversation b)
    {
        if (a.RepositoryKey.Equals(b.RepositoryKey, StringComparison.OrdinalIgnoreCase)) return true;
        static bool Contains(string parent, string child)
        {
            parent = Path.GetFullPath(parent);
            child = Path.GetFullPath(child);
            return child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
                child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        return Contains(a.Workspace, b.Workspace) || Contains(b.Workspace, a.Workspace);
    }

    private async Task StartAsync(Conversation c, CancellationToken ct)
    {
        try
        {
            var thread = await client.ReadAsync(c.Id, ct);
            ValidateWorkspace(c, thread);
            ApplyThread(c, thread);
            if (!c.Armed || c.Attention || c.ExecutionActive || !state.Enabled || !ownershipStillValid()) return;
            c.RepositoryKey = await repositoryKey(thread.Workspace, ct);
            if (!CanStart(c)) return;
            thread = await client.ResumeAsync(c.Id, ct);
            ValidateWorkspace(c, thread);
            ApplyThread(c, thread);
            if (!c.Armed || c.Attention || c.ExecutionActive || thread.RuntimeStatus != "idle" || !ownershipStillValid()) return;
            var attempt = new ResumeAttempt { ThreadId = c.Id, BaseTurnId = thread.LastTurn?.Id, CreatedAt = now(), Phase = AttemptPhase.Prepared };
            state.Attempts.Add(attempt); c.AttemptId = attempt.Id; c.ExecutionActive = true;
            c.StartedAt = now(); c.Status = ConversationStatus.Running; c.Reason = "Submitting continuation";
            Commit(); // The durable intent MUST exist before the first byte of turn/start.
            var turn = await client.StartTurnAsync(c.Id, c.Prompt, attempt.Id, ct);
            if (string.IsNullOrWhiteSpace(turn.Id) || turn.Id == attempt.BaseTurnId)
                throw new InvalidDataException("Codex did not return a new turn identity; possible external activity. Inspect before retrying.");
            attempt.TurnId = turn.Id; attempt.Phase = AttemptPhase.Accepted;
            c.LastTurnId = turn.Id; c.StartedAt = turn.StartedAt ?? c.StartedAt; c.Reason = "Executing continuation";
            Log($"Resumed {c.Name}; attempt {attempt.Id}, turn {turn.Id}."); Notice?.Invoke($"Resumed {c.Name}");
            ApplyTurn(c, turn); Commit();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Even a timeout or RPC error after dispatch can be ambiguous. Do not resend.
            Attention(c, ex.Message + (Uncertain(c) ? " Delivery may be uncertain; inspect before rearming." : ""));
            Log($"Resume needs attention for {c.Name}: {c.Reason}"); Notice?.Invoke($"Needs attention: {c.Name}");
        }
    }

    private async Task RevalidateAllAsync(CancellationToken ct)
    {
        foreach (var c in state.Conversations)
        {
            try
            {
                var thread = await client.ReadAsync(c.Id, ct);
                ValidateWorkspace(c, thread);
                var key = await repositoryKey(thread.Workspace, ct);
                c.RepositoryKey = key;
                ApplyThread(c, thread);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                c.Revalidated = false;
                Attention(c, "Revalidation failed: " + ex.Message);
            }
        }
        UpdatePresentation();
    }

    private static void ValidateWorkspace(Conversation c, ThreadSnapshot thread)
    {
        AppServerClient.ValidateId(c.Id, thread.Id);
        if (!Path.GetFullPath(c.Workspace).Equals(Path.GetFullPath(thread.Workspace), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Codex workspace changed. Remove and select the conversation again after reviewing its new path.");
    }

    private void ApplyThread(Conversation c, ThreadSnapshot thread)
    {
        c.Revalidated = true;
        if (thread.RuntimeStatus is not ("active" or "idle" or "notLoaded" or "systemError"))
        { Attention(c, "Unsupported Codex runtime status: " + thread.RuntimeStatus); return; }
        c.ExecutionActive = thread.RuntimeStatus == "active" || thread.LastTurn?.Status == "inProgress";
        if (thread.ActiveFlags.Length > 0) { Attention(c, "Codex requires intervention: " + string.Join(", ", thread.ActiveFlags)); return; }
        if (thread.RuntimeStatus == "systemError") { Attention(c, "Codex reports a thread system error."); return; }
        var attempt = Attempt(c);
        if (attempt?.Phase == AttemptPhase.Prepared && attempt.TurnId == null)
        { Attention(c, "A journaled continuation has uncertain delivery. Inspect the conversation before explicitly reconciling and rearming."); return; }
        if (attempt?.Phase == AttemptPhase.Accepted && thread.LastTurn?.Id != attempt.TurnId)
        { Attention(c, "The latest turn differs from the journaled turn. Inspect external activity; no automatic retry."); return; }
        if (c.LastTurnId != null && thread.LastTurn is { } latest && c.LastTurnId != latest.Id && attempt?.TurnId != latest.Id)
            Attention(c, "The latest turn changed outside AutoResume. Inspect external activity before explicitly rearming.");
        if (thread.LastTurn is { } turn)
        {
            // An explicit rearm permits the current historical terminal turn. A new
            // successful turn always clears Armed, even if the completion event was lost.
            if (c.Armed && c.ArmedAfterTurnId == turn.Id && !c.ExecutionActive && attempt?.Phase != AttemptPhase.Accepted) return;
            ApplyTurn(c, turn);
            if (turn.Status == "inProgress" && thread.RuntimeStatus == "notLoaded")
                Attention(c, "History ends in an unfinished turn, but this server does not own a live execution. Resolve its state before retrying.");
        }
        else if (!c.Armed) { c.Status = ConversationStatus.Paused; c.Reason = "No interrupted work; explicitly rearm to continue"; }
    }

    private void ApplyTurn(Conversation c, TurnSnapshot turn)
    {
        c.LastTurnId = turn.Id;
        var attempt = Attempt(c);
        if (attempt?.TurnId != turn.Id) attempt = null;
        switch (turn.Status)
        {
            case "inProgress":
                c.ExecutionActive = true; c.StartedAt = turn.StartedAt ?? c.StartedAt;
                if (!c.Attention) { c.Status = ConversationStatus.Running; c.Reason = "Executing"; }
                break;
            case "completed":
                var newlyCompleted = c.Status != ConversationStatus.Completed;
                c.ExecutionActive = false; c.Armed = false; c.WaitingForUsage = false; c.ResetAt = c.NextCheckAt = null;
                if (turn.CompletedAt != null) { c.CompletedAt = turn.CompletedAt; c.CompletionTimeEstimated = false; }
                else if (c.CompletedAt == null) { c.CompletedAt = now(); c.CompletionTimeEstimated = true; }
                if (attempt != null) attempt.Phase = AttemptPhase.Completed;
                if (!c.Attention) { c.Status = ConversationStatus.Completed; c.Reason = "Work completed; no further continuation will be sent"; }
                if (newlyCompleted && !c.Attention) { Log($"Completed {c.Name}."); Notice?.Invoke($"Completed {c.Name}"); }
                break;
            case "failed":
            case "interrupted":
                c.ExecutionActive = false;
                if (Protocol.IsLimit(turn.ErrorCode))
                {
                    if (attempt != null) attempt.Phase = AttemptPhase.RateLimited;
                    c.Armed = true; c.ArmedAfterTurnId = turn.Id;
                    Limit(c, usage?.ResetAt, turn.ErrorMessage ?? "Codex usage limit reached");
                }
                else
                {
                    if (attempt != null) attempt.Phase = AttemptPhase.Failed;
                    if (turn.Status == "interrupted" && attempt == null && !c.Attention)
                    {
                        c.Armed = true; c.ArmedAfterTurnId = turn.Id; c.Status = ConversationStatus.Queued; c.Reason = "Interrupted work ready to continue";
                    }
                    else Attention(c, turn.ErrorMessage ?? "Turn stopped. Resolve the cause, then explicitly retry.");
                }
                break;
            default: Attention(c, "Unsupported Codex turn status: " + turn.Status); break;
        }
    }

    private void Limit(Conversation c, DateTimeOffset? resetAt, string reason, bool extendBackoff = false)
    {
        var changed = c.Status != ConversationStatus.RateLimited;
        c.WaitingForUsage = true; c.Status = ConversationStatus.RateLimited; c.ResetAt = resetAt; c.Reason = reason;
        if (c.NextCheckAt == null || (extendBackoff && c.NextCheckAt <= now()))
        {
            c.NextCheckAt = resetAt > now() ? resetAt.Value.AddSeconds(5) : now().AddMinutes(c.BackoffMinutes);
            if (extendBackoff) c.BackoffMinutes = Math.Min(60, c.BackoffMinutes * 2);
        }
        if (changed) { Log($"Rate limited: {c.Name}. {reason}"); Notice?.Invoke($"Rate limited: {c.Name}"); }
    }

    private async Task ProcessEventsAsync()
    {
        try
        {
            await foreach (var e in events.Reader.ReadAllAsync(lifetime.Token))
            {
                await InGateAsync(async () =>
                {
                    if (e.Method == "connection/lost")
                    {
                        connected = false; state.Enabled = false;
                        foreach (var row in state.Conversations) { row.Revalidated = false; Attention(row, "Connection lost. Restart and reconcile before retrying."); }
                        Log("App-server disconnected; scheduling OFF."); Commit(); return;
                    }
                    var id = Protocol.String(e.Parameters, "threadId");
                    var c = state.Conversations.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    try
                    {
                    if (e.RequestId != null)
                    {
                        if (c != null) Attention(c, "Manual intervention required: " + e.Method + ". Request declined; resolve in a supported client, then retry.");
                        else { state.Enabled = false; Log("Unattributed server request; scheduling stopped."); }
                        await client.RejectRequestAsync(e, lifetime.Token);
                        Notice?.Invoke(c == null ? "Codex requires intervention" : $"Needs attention: {c.Name}");
                    }
                    else if (c != null && e.Method is "turn/started" or "turn/completed")
                    {
                        var turn = Protocol.Turn(e.Parameters.GetProperty("turn"));
                        var attempt = Attempt(c);
                        if (turn.Status != "inProgress" && c.Armed && c.ArmedAfterTurnId == turn.Id && attempt?.Phase != AttemptPhase.Accepted) return;
                        // Ignore delayed notifications for earlier attempts; never let one
                        // conversation or obsolete turn finish another execution.
                        if (attempt?.TurnId is { } expected && expected != turn.Id) return;
                        if (attempt == null && c.LastTurnId != null && c.LastTurnId != turn.Id)
                            Attention(c, "Unexpected external turn activity. Exclusive ownership may have been violated.");
                        else ApplyTurn(c, turn);
                    }
                    else if (c != null && e.Method == "error")
                    {
                        if (!c.ExecutionActive) return;
                        var turnId = Protocol.String(e.Parameters, "turnId");
                        if (Attempt(c)?.TurnId is { } expected && expected != turnId) return;
                        var error = e.Parameters.GetProperty("error");
                        // Error notifications can be transient. Keep the active turn in
                        // flight until turn/completed or a read establishes a terminal state.
                        c.Reason = Protocol.String(error, "message") ?? "Codex execution error; checking terminal state";
                    }
                    else if (e.Method == "thread/status/changed" && c != null)
                    {
                        var thread = await client.ReadAsync(c.Id, lifetime.Token);
                        ValidateWorkspace(c, thread); ApplyThread(c, thread);
                    }
                    else if (e.Method == "account/rateLimits/updated")
                    {
                        // Notifications have windows but may omit ordinaryUsageAllowed.
                        // Use their reset estimates immediately, never as permission.
                        var update = Protocol.Usage(e.Parameters);
                        usage = update;
                        foreach (var row in state.Conversations.Where(x => x.Armed && !x.ExecutionActive && !x.Attention))
                        {
                            if (update.Exhausted)
                            {
                                Limit(row, update.ResetAt ?? row.ResetAt, update.Detail);
                                if (update.ResetAt > now()) row.NextCheckAt = update.ResetAt.Value.AddSeconds(5);
                            }
                            else if (row.WaitingForUsage)
                                row.NextCheckAt = usageCheckedAt.AddSeconds(30) > now() ? usageCheckedAt.AddSeconds(30) : now();
                        }
                        nextPoll = DateTimeOffset.MinValue;
                    }
                    }
                    catch (Exception ex) when (c != null && ex is not OperationCanceledException)
                    {
                        c.Revalidated = false;
                        Attention(c, "Conversation event could not be reconciled: " + ex.Message);
                        Log($"Needs attention for {c.Name}: {c.Reason}");
                    }
                    UpdatePresentation(); Commit();
                }, lifetime.Token);
                await TickAsync(lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { await InGateAsync(() => { FailClosed(ex); return Task.CompletedTask; }, CancellationToken.None); }
    }

    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try { while (await timer.WaitForNextTickAsync(lifetime.Token)) await TickAsync(lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { await InGateAsync(() => { FailClosed(ex); return Task.CompletedTask; }, CancellationToken.None); }
    }

    private void UpdatePresentation()
    {
        foreach (var c in state.Conversations)
        {
            if (c.Attention) { c.Status = ConversationStatus.NeedsAttention; continue; }
            if (c.ExecutionActive) { c.Status = ConversationStatus.Running; continue; }
            if (c.Status == ConversationStatus.Completed) continue;
            if (c.Paused) { c.Status = ConversationStatus.Paused; continue; }
            if (c.WaitingForUsage) { c.Status = ConversationStatus.RateLimited; continue; }
            c.Status = !c.Revalidated || c.Paused || !c.Armed || !state.Enabled ? ConversationStatus.Paused : ConversationStatus.Queued;
        }
    }

    private void Attention(Conversation c, string reason)
    {
        c.Attention = true; c.Status = ConversationStatus.NeedsAttention; c.Reason = reason;
    }
    private ResumeAttempt? Attempt(Conversation c) => state.Attempts.LastOrDefault(a => a.Id == c.AttemptId);
    private bool Uncertain(Conversation c) => Attempt(c)?.Phase is AttemptPhase.Prepared or AttemptPhase.Accepted;
    private Conversation Find(string id) => state.Conversations.Single(c => c.Id == id);
    private void Log(string text)
    {
        state.Log.Insert(0, $"{now():u}  {text}");
        if (state.Log.Count > 200) state.Log.RemoveRange(200, state.Log.Count - 200);
    }
    private void RequireConnection()
    {
        if (!connected || !ownershipConfirmed || storageFault) throw new InvalidOperationException("Connect with exclusive session ownership before scheduling.");
    }
    private void Commit()
    {
        try { store.Save(state); }
        catch { storageFault = true; state.Enabled = false; throw; }
        Changed?.Invoke(StateJson.Clone(state));
    }
    private void FailClosed(Exception ex)
    {
        state.Enabled = false;
        foreach (var c in state.Conversations) Attention(c, "Watcher stopped: " + ex.Message);
        Log("Watcher stopped; restart after resolving the failure.");
        try { Commit(); } catch { Changed?.Invoke(StateJson.Clone(state)); }
        Notice?.Invoke("AutoResume stopped: " + ex.Message);
    }
    private async Task InGateAsync(Func<Task> action, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { await action(); }
        finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        foreach (var task in new[] { eventTask, pollTask })
            if (task != null) try { await task; } catch (OperationCanceledException) { }
        await client.DisposeAsync();
        lifetime.Dispose(); gate.Dispose();
    }
}
