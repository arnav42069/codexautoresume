namespace CodexAutoResume.Core;

/// <summary>Serialized, read-only recovery. It never acquires execution ownership.</summary>
public sealed class AutoConnection(Scheduler scheduler,
    Func<string?, bool, CancellationToken, Task<DetectedCodex>>? discover = null,
    Func<DateTimeOffset>? clock = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Func<string?, bool, CancellationToken, Task<DetectedCodex>> find = discover ??
        ((path, manual, ct) => CodexDiscovery.DiscoverAsync(path, manual, ct));
    private int failures;
    public bool IsConnecting { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? NextRetryAt { get; private set; }
    public DetectedCodex? Detected { get; private set; }
    public string Status => IsConnecting ? "Detecting Codex and connecting read-only…" :
        scheduler.IsConnected ? scheduler.HasExecutionOwnership ? "Connected · execution ownership acquired" : "Connected read-only · Desktop may remain open" :
        NextRetryAt is { } retry ? $"Disconnected · retry in {Math.Max(0, (int)Math.Ceiling((retry - now()).TotalSeconds))}s · scheduling OFF" :
        "Disconnected · automatic connection pending · scheduling OFF";

    public async Task<bool> TryConnectAsync(string? path, bool manualOverride = false,
        bool force = false, CancellationToken ct = default)
    {
        if (!await gate.WaitAsync(0, ct)) return false;
        try
        {
            if (scheduler.IsConnected || !force && NextRetryAt > now()) return false;
            IsConnecting = true;
            try
            {
                var found = await find(path, manualOverride, ct);
                await scheduler.ConnectForDiscoveryAsync(found.Path, ct);
                Detected = found; Error = null; NextRetryAt = null; failures = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Error = ex.Message;
                int[] seconds = [5, 10, 30, 60];
                NextRetryAt = now().AddSeconds(seconds[Math.Min(failures++, seconds.Length - 1)]);
            }
            finally { IsConnecting = false; }
            return true;
        }
        finally { gate.Release(); }
    }
}
