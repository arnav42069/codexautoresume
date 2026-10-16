namespace CodexAutoResume.Core;

public sealed partial class Scheduler
{
    public async Task ConnectForDiscoveryAsync(string executable, CancellationToken ct = default)
    {
        await InGateAsync(async () =>
        {
            if (connected) return;
            await client.ConnectAsync(executable, ct);
            connected = true; ownershipConfirmed = false; state.Enabled = false;
            state.CodexPath = executable;
            foreach (var row in state.Conversations.Where(c => !c.ExecutionActive && !Uncertain(c)))
            { row.ObservationOnly = true; row.Revalidated = false; }
            Log("Connected read-only. Desktop may remain open; no conversation will be resumed or started.");
            await RefreshObservationCoreAsync(ct);
        }, ct);
        eventTask ??= ProcessEventsAsync();
        if (backgroundPolling) pollTask ??= PollAsync();
    }

    public Task AcquireExecutionOwnershipAsync(bool exclusiveOwnership, CancellationToken ct = default) => InGateAsync(async () =>
    {
        RequireDiscoveryConnection();
        if (!exclusiveOwnership || !ownershipStillValid())
            throw new InvalidOperationException("Discovery can stay connected with Desktop open. For execution, finish active work, close other Codex clients and confirm exclusive ownership.");
        if (ownershipConfirmed) return;
        state.Enabled = false; ownershipConfirmed = true;
        foreach (var row in state.Conversations) row.ObservationOnly = false;
        Log("Execution ownership acquired; revalidating exact UUIDs. Scheduling remains OFF.");
        await RevalidateAllAsync(ct);
        nextPoll = now().AddSeconds(30); Commit();
    }, ct);

    public Task RefreshDiscoveryAsync(CancellationToken ct = default) => InGateAsync(async () =>
    {
        RequireDiscoveryConnection();
        if (ownershipConfirmed) throw new InvalidOperationException("Recent-chat refresh is available in read-only mode; execution priorities stay fixed.");
        await RefreshObservationCoreAsync(ct);
    }, ct);

    private async Task RefreshObservationCoreAsync(CancellationToken ct)
    {
        nextPoll = now().AddSeconds(30);
        try
        {
            var listing = await client.ListRecentAsync(ct);
            state.DiscoveryOrder = listing.OrderDescription;
            state.LastDiscoveryAt = now(); state.DiscoveryError = null;
            if (autoFillAllowed && state.Conversations.Count == 0)
            {
                var imported = new List<Conversation>();
                foreach (var thread in listing.Threads.Take(5))
                {
                    if (!Guid.TryParse(thread.Id, out var id) || !Path.IsPathFullyQualified(thread.Workspace))
                        throw new InvalidDataException("Codex returned an invalid existing chat identity or workspace.");
                    if (imported.Any(c => c.Id == id.ToString())) continue;
                    var row = new Conversation { Id = id.ToString(), Name = thread.Name, Workspace = thread.Workspace, ObservationOnly = true };
                    Observe(row, thread); imported.Add(row);
                }
                state.Conversations.AddRange(imported);
                if (imported.Count > 0) { autoFillAllowed = false; Log($"Selected {imported.Count} recent chats, unarmed, in read-only mode."); }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { state.DiscoveryError = "Recent chat discovery failed: " + ex.Message; }
        // List failures must not prevent reconciliation of selected UUIDs/owned attempts.
        foreach (var row in state.Conversations)
        {
            try
            {
                var thread = await client.ReadAsync(row.Id, ct); AppServerClient.ValidateId(row.Id, thread.Id);
                if (row.ExecutionActive || Uncertain(row))
                { ValidateWorkspace(row, thread); ApplyThread(row, thread); }
                else Observe(row, thread);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                row.ObservedActive = false; row.Revalidated = false;
                if (!row.ExecutionActive && !Uncertain(row)) row.ObservationOnly = true;
                row.Reason = "Cannot observe this conversation: " + ex.Message + ". No continuation is permitted.";
            }
        }
        Commit();
    }

    private void Observe(Conversation row, ThreadSnapshot thread)
    {
        ValidateWorkspace(row, thread);
        row.ObservationOnly = true; row.Revalidated = false; row.ObservedAt = now();
        row.ObservedActive = thread.VerifiedActive; row.ObservedTurnStatus = thread.LastTurn?.Status;
        row.LastUsedAt = thread.RecentActivityAt ?? row.LastUsedAt;
        if (!row.Attention) row.Reason = (row.ObservedActive ? "Active on the connected server. " : "Desktop activity is unknown. ") +
            (row.ObservedTurnStatus == null ? "" : $"Last recorded turn: {row.ObservedTurnStatus}. ") +
            "Read-only; acquire execution ownership to revalidate before scheduling.";
        // Observation never changes Armed, LastTurnId, attempts or execution reservations.
    }
}
