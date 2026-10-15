using System.Text.Json;
using CodexAutoResume.Core;

namespace CodexAutoResume.Tests;

internal sealed class MemoryStore : IStateStore
{
    public AppState State = new();
    public bool Fail;
    public AppState Load() => StateJson.Clone(State);
    public void Save(AppState state)
    {
        if (Fail) throw new IOException("Simulated disk failure");
        State = StateJson.Clone(state);
    }
}

internal sealed class FakeCodex : ICodexClient
{
    public readonly Dictionary<string, ThreadSnapshot> Threads = [];
    public readonly List<string> Starts = [];
    public UsageSnapshot Usage = new(true, false, null, "Available");
    public int UsageReads, Rejections;
    public Action<string>? BeforeStart;
    public bool LoseResponse, MismatchRead, MismatchResume;
    public string? NextTurnError;
    public string? ReadFailureId;
    public event Action<CodexEvent>? EventReceived;
    public event Action<string>? Disconnected;
    public Task ConnectAsync(string executable, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ThreadSnapshot>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ThreadSnapshot>>(Threads.Values.ToList());
    public Task<ThreadSnapshot> ReadAsync(string id, CancellationToken ct) => id == ReadFailureId
        ? throw new IOException("Session unavailable")
        : Task.FromResult(MismatchRead ? Threads[id] with { Id = Guid.NewGuid().ToString() } : Threads[id]);
    public Task<ThreadSnapshot> ResumeAsync(string id, CancellationToken ct)
    {
        if (MismatchResume) return Task.FromResult(Threads[id] with { Id = Guid.NewGuid().ToString() });
        Threads[id] = Threads[id] with { RuntimeStatus = "idle" };
        return Task.FromResult(Threads[id]);
    }
    public Task<TurnSnapshot> StartTurnAsync(string id, string prompt, string attemptId, CancellationToken ct)
    {
        BeforeStart?.Invoke(id);
        Starts.Add(id);
        var turn = new TurnSnapshot(Guid.NewGuid().ToString(), NextTurnError == null ? "inProgress" : "failed", NextTurnError);
        Threads[id] = Threads[id] with { RuntimeStatus = turn.Status == "inProgress" ? "active" : "idle", LastTurn = turn };
        if (LoseResponse) throw new IOException("Response lost after dispatch");
        return Task.FromResult(turn);
    }
    public Task<UsageSnapshot> ReadUsageAsync(CancellationToken ct) { UsageReads++; return Task.FromResult(Usage); }
    public Task RejectRequestAsync(CodexEvent request, CancellationToken ct) { Rejections++; return Task.CompletedTask; }
    public void Emit(string method, object parameters, bool request = false) => EventReceived?.Invoke(new(method, JsonSerializer.SerializeToElement(parameters), request ? JsonSerializer.SerializeToElement(9) : null));
    public void Complete(string id, string status = "completed", string? code = null)
    {
        var turn = Threads[id].LastTurn! with { Status = status, ErrorCode = code };
        Threads[id] = Threads[id] with { RuntimeStatus = "idle", LastTurn = turn };
        Emit("turn/completed", new { threadId = id, turn = new { id = turn.Id, status, error = code == null ? null : new { message = "Limit", codexErrorInfo = code } } });
    }
    public void Disconnect() => Disconnected?.Invoke("Disconnected");
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class Rig : IAsyncDisposable
{
    public readonly FakeCodex Client = new();
    public readonly MemoryStore Store;
    public readonly Scheduler Scheduler;
    public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-14T10:00:00Z");
    public bool Owned = true;
    public Rig(MemoryStore? store = null)
    {
        Store = store ?? new();
        Scheduler = new(Client, Store, () => Now, (cwd, _) => Task.FromResult(cwd), () => Owned, backgroundPolling: false);
    }
    public Task Connect() => Scheduler.ConnectAsync("fake", true);
    public async Task<string> Add(string? workspace = null, bool parallel = false, string status = "interrupted", string? code = null)
    {
        var id = Guid.NewGuid().ToString();
        Client.Threads[id] = new(id, "Test " + id[..4], workspace ?? "/repo/" + id, "idle", [], new(Guid.NewGuid().ToString(), status, code));
        await Scheduler.AddAsync(id);
        if (parallel) await Scheduler.UpdateAsync(id, c => c.Parallel = true);
        return id;
    }
    public async Task Enable() { await Scheduler.SetEnabledAsync(true); await Scheduler.TickAsync(); }
    public Task<AppState> State() => Scheduler.GetSnapshotAsync();
    public async Task WaitFor(Func<AppState, bool> predicate)
    {
        for (int i = 0; i < 300; i++)
        {
            if (predicate(await State())) return;
            await Task.Delay(10);
        }
        throw new Exception("Timed out waiting for scheduler event");
    }
    public ValueTask DisposeAsync() => Scheduler.DisposeAsync();
}
