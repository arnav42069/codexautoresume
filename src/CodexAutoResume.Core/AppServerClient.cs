using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace CodexAutoResume.Core;

public sealed class RpcException(string message, JsonElement data) : Exception(message)
{
    public JsonElement DataElement { get; } = data;
}

public sealed partial class AppServerClient : ICodexClient
{
    private Process? process;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private long nextId;
    private Task? reader;
    public event Action<CodexEvent>? EventReceived;
    public event Action<string>? Disconnected;
    public string Version { get; private set; } = "";
    public int? ProcessId => process?.Id;

    public async Task ConnectAsync(string executable, CancellationToken ct)
    {
        if (process != null) throw new InvalidOperationException("Already connected. Restart the utility to change its Codex executable.");
        if (OperatingSystem.IsWindows()) executable = CodexExecutableResolver.Resolve(executable);
        var versionInfo = NewProcess(executable);
        versionInfo.ArgumentList.Add("--version");
        using (var version = Process.Start(versionInfo) ?? throw new IOException("Cannot launch Codex."))
        {
            var output = version.StandardOutput.ReadToEndAsync(ct);
            var errors = version.StandardError.ReadToEndAsync(ct);
            await version.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(15), ct);
            Version = (await output).Trim();
            await errors;
            if (version.ExitCode != 0) throw new IOException("codex --version failed. Install the native Codex CLI and sign in separately.");
        }
        var info = NewProcess(executable);
        info.ArgumentList.Add("app-server");
        info.ArgumentList.Add("--listen");
        info.ArgumentList.Add("stdio://");
        process = Process.Start(info) ?? throw new IOException("Cannot start Codex app-server.");
        // Drain stderr to avoid a blocked child. Never expose configuration or credential output.
        _ = DrainErrorsAsync(process.StandardError);
        reader = ReadLoopAsync();
        await CallAsync("initialize", new { clientInfo = new { name = "codex_autoresume", title = "Codex AutoResume", version = "0.1.1" } }, ct);
        await WriteAsync(new { method = "initialized", @params = new { } }, ct);
    }

    private static ProcessStartInfo NewProcess(string executable) => new(executable)
    {
        UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
        RedirectStandardError = true, CreateNoWindow = true
    };

    private static async Task DrainErrorsAsync(StreamReader errors)
    {
        try { while (await errors.ReadLineAsync() != null) { } }
        catch (IOException) { }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var line = await process!.StandardOutput.ReadLineAsync(lifetime.Token);
                if (line == null) throw new IOException("Codex app-server disconnected. Reconcile attempts before retrying.");
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("method", out var method))
                {
                    var parameters = root.TryGetProperty("params", out var p) ? p.Clone() : JsonSerializer.SerializeToElement(new { });
                    EventReceived?.Invoke(new(method.GetString()!, parameters,
                        root.TryGetProperty("id", out var reqId) ? reqId.Clone() : null));
                }
                else if (root.TryGetProperty("id", out var id) && id.TryGetInt64(out var key) && pending.TryRemove(key, out var completion))
                {
                    if (root.TryGetProperty("error", out var error))
                        completion.TrySetException(new RpcException(Protocol.String(error, "message") ?? "Codex request failed.", error.Clone()));
                    else if (root.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
                    else completion.TrySetException(new InvalidDataException("Malformed Codex response."));
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            foreach (var pair in pending) pair.Value.TrySetException(new IOException("Codex connection lost; delivery may be uncertain.", ex));
            pending.Clear();
            if (!lifetime.IsCancellationRequested) Disconnected?.Invoke("Codex connection lost; automatic scheduling stopped.");
        }
    }

    private async Task WriteAsync(object message, CancellationToken ct)
    {
        await writer.WaitAsync(ct);
        try
        {
            if (process == null || process.HasExited) throw new IOException("Codex app-server is not connected.");
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), ct);
            await process.StandardInput.FlushAsync(ct);
        }
        finally { writer.Release(); }
    }

    private async Task<JsonElement> CallAsync(string method, object parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await WriteAsync(new { id, method, @params = parameters }, ct);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(45), ct);
        }
        finally { pending.TryRemove(id, out _); }
    }

    public async Task<IReadOnlyList<ThreadSnapshot>> ListAsync(CancellationToken ct)
    {
        var result = new List<ThreadSnapshot>();
        string? cursor = null;
        var cursors = new HashSet<string>();
        do
        {
            var response = await CallAsync("thread/list", new { cursor, limit = 100 }, ct);
            result.AddRange(response.GetProperty("data").EnumerateArray().Select(Protocol.Thread));
            cursor = Protocol.String(response, "nextCursor");
            if (cursor != null && !cursors.Add(cursor)) throw new InvalidDataException("Codex returned a repeated pagination cursor.");
        } while (cursor != null);
        return result;
    }

    public async Task<ThreadSnapshot> ReadAsync(string id, CancellationToken ct)
    {
        var response = await CallAsync("thread/read", new { threadId = id, includeTurns = false }, ct);
        var thread = Protocol.Thread(response.GetProperty("thread"));
        ValidateId(id, thread.Id);
        var turns = await CallAsync("thread/turns/list", new { threadId = id, limit = 1, sortDirection = "desc" }, ct);
        var last = turns.GetProperty("data").EnumerateArray().FirstOrDefault();
        return thread with { LastTurn = last.ValueKind == JsonValueKind.Object ? Protocol.Turn(last) : null };
    }

    public async Task<ThreadSnapshot> ResumeAsync(string id, CancellationToken ct)
    {
        // No overrides: preserve the user's authentication, model, approvals and sandbox.
        var response = await CallAsync("thread/resume", new { threadId = id }, ct);
        var thread = Protocol.Thread(response.GetProperty("thread"));
        ValidateId(id, thread.Id);
        return await ReadAsync(id, ct);
    }

    public async Task<TurnSnapshot> StartTurnAsync(string id, string prompt, string attemptId, CancellationToken ct)
    {
        var response = await CallAsync("turn/start", new { threadId = id, clientUserMessageId = attemptId,
            input = new[] { new { type = "text", text = prompt } } }, ct);
        // turn/start returns a Turn, without threadId. Identity is established by the exact
        // resume response and request target; do not invent a returned thread UUID.
        return Protocol.Turn(response.GetProperty("turn"));
    }

    public async Task<UsageSnapshot> ReadUsageAsync(CancellationToken ct) =>
        Protocol.Usage(await CallAsync("account/rateLimits/read", new { }, ct));

    public Task RejectRequestAsync(CodexEvent request, CancellationToken ct)
    {
        object message = request.Method switch
        {
            "item/commandExecution/requestApproval" or "item/fileChange/requestApproval" =>
                new { id = request.RequestId, result = (object)new { decision = "decline" } },
            "item/permissions/requestApproval" =>
                new { id = request.RequestId, result = (object)new { permissions = new { }, scope = "turn" } },
            _ => new { id = request.RequestId, error = new { code = -32601, message = "Manual intervention required in a supported Codex client; AutoResume does not approve requests." } }
        };
        return WriteAsync(message, ct);
    }

    public static void ValidateId(string requested, string actual)
    {
        if (!Guid.TryParse(requested, out var expected) || !Guid.TryParse(actual, out var returned) || expected != returned)
            throw new InvalidDataException("Codex returned a different thread UUID. No continuation was sent.");
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if (process != null)
        {
            process.StandardInput.Close();
            if (reader != null) await reader;
            process.Dispose();
        }
        lifetime.Dispose();
        writer.Dispose();
    }
}

public static class Protocol
{
    public static string? String(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static DateTimeOffset? Timestamp(JsonElement element, string name) => element.TryGetProperty(name, out var value) &&
        value.TryGetInt64Safe(out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    private static bool TryGetInt64Safe(this JsonElement element, out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }
    public static ThreadSnapshot Thread(JsonElement value)
    {
        var status = value.GetProperty("status");
        return new(value.GetProperty("id").GetString()!, String(value, "name") ?? String(value, "preview") ?? "Untitled conversation",
            value.GetProperty("cwd").GetString()!, status.GetProperty("type").GetString()!,
            status.TryGetProperty("activeFlags", out var flags) ? flags.EnumerateArray().Select(f => f.GetString()!).ToArray() : [], null);
    }
    public static TurnSnapshot Turn(JsonElement value)
    {
        string? code = null, message = null;
        if (value.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            message = String(error, "message");
            if (error.TryGetProperty("codexErrorInfo", out var info)) code = ErrorCode(info);
        }
        return new(value.GetProperty("id").GetString()!, value.GetProperty("status").GetString()!, code, message,
            Timestamp(value, "startedAt"), Timestamp(value, "completedAt"));
    }
    public static string? ErrorCode(JsonElement info)
    {
        if (info.ValueKind == JsonValueKind.String) return info.GetString();
        if (info.ValueKind == JsonValueKind.Object)
            foreach (var property in info.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty("httpStatusCode", out var http) &&
                    http.ValueKind == JsonValueKind.Number && http.GetInt32() == 429) return "rateLimitExceeded";
        return null;
    }
    public static bool IsLimit(string? code) => code is "rateLimitExceeded" or "usageLimitExceeded";

    public static UsageSnapshot Usage(JsonElement response)
    {
        bool? allowed = response.TryGetProperty("ordinaryUsageAllowed", out var permission) &&
            permission.ValueKind is JsonValueKind.True or JsonValueKind.False ? permission.GetBoolean() : null;
        var buckets = new List<JsonElement>();
        if (response.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object)
            buckets.AddRange(byId.EnumerateObject().Select(p => p.Value));
        if (response.TryGetProperty("rateLimits", out var single) && single.ValueKind == JsonValueKind.Object) buckets.Add(single);
        bool exhausted = allowed == false;
        var resets = new List<DateTimeOffset>();
        foreach (var bucket in buckets)
        {
            if (bucket.TryGetProperty("spendControlReached", out var spend) && spend.ValueKind == JsonValueKind.True) exhausted = true;
            foreach (var key in new[] { "primary", "secondary" })
                if (bucket.TryGetProperty(key, out var window) && window.ValueKind == JsonValueKind.Object &&
                    window.TryGetProperty("usedPercent", out var percent) && percent.GetInt32() >= 100)
                {
                    exhausted = true;
                    if (Timestamp(window, "resetsAt") is { } reset) resets.Add(reset);
                }
        }
        return new(allowed, exhausted, resets.Count > 0 ? resets.Max() : null,
            exhausted ? "Included usage unavailable" : allowed == true ? "Included usage available" : "Usage permission unavailable; automatic recovery requires ordinaryUsageAllowed=true");
    }
}
