using System.Text.Json;
using CodexAutoResume.Core;
using CodexAutoResume.Tests;

if (args.FirstOrDefault() == "--version") { Console.WriteLine("codex-cli 0.159.2 fixture"); return 0; }
if (args.FirstOrDefault() == "app-server") { await ProtocolFixture.Run(); return 0; }

if (args.FirstOrDefault() == "--live-smoke")
{
    await using var live = new AppServerClient();
    await live.ConnectAsync(args.ElementAtOrDefault(1) ?? "codex", CancellationToken.None);
    var threads = await live.ListAsync(CancellationToken.None);
    Console.WriteLine($"PASS real app-server initialization and paginated discovery ({live.Version}; {threads.Count} sessions). No turns submitted.");
    return 0;
}

var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Func<Task> run) => tests.Add((name, run));
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
void True(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
async Task Throws(Func<Task> run) { try { await run(); } catch { return; } throw new Exception("Expected an exception"); }
JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

void WithCodexInstallation(Action<string, Func<string[], string>> check)
{
    var directory = Path.Combine(Path.GetTempPath(), "codex locator & test-" + Guid.NewGuid());
    Directory.CreateDirectory(directory);
    string Add(params string[] parts)
    {
        var path = Path.Combine(new[] { directory }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture only; not an executable");
        return path;
    }
    try { check(directory, Add); }
    finally { Directory.Delete(directory, recursive: true); }
}

Test("find native Codex in the current nested npm Windows layout", () =>
{
    WithCodexInstallation((root, add) =>
    {
        add(["codex.cmd"]);
        var executable = add(["node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe"]);
        Equal(executable, CodexExecutableResolver.Find("codex.exe", [root]));
        Equal(executable, CodexExecutableResolver.Find(Path.Combine(root, "codex.cmd"), []));
    });
    return Task.CompletedTask;
});
Test("find native Codex in the bundled npm vendor layout", () =>
{
    WithCodexInstallation((root, add) =>
    {
        var executable = add(["node_modules", "@openai", "codex", "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe"]);
        Equal(executable, CodexExecutableResolver.Find("codex", [root]));
    });
    return Task.CompletedTask;
});
Test("find native Codex in a hoisted optional npm package", () =>
{
    WithCodexInstallation((root, add) =>
    {
        var executable = add(["node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe"]);
        Equal(executable, CodexExecutableResolver.Find(null, [root]));
    });
    return Task.CompletedTask;
});
Test("native Codex on PATH takes precedence and quoted paths with spaces work", () =>
{
    WithCodexInstallation((root, add) =>
    {
        var first = add(["first", "codex.exe"]); add(["second", "codex.exe"]);
        Equal(first, CodexExecutableResolver.Find("codex.exe", ["\"" + Path.Combine(root, "first") + "\"", Path.Combine(root, "second")]));
        Equal(first, CodexExecutableResolver.Find("\"" + first + "\"", []));
    });
    return Task.CompletedTask;
});
Test("missing explicit Codex executable never silently switches installations", () =>
{
    WithCodexInstallation((root, add) =>
    {
        add(["codex.exe"]);
        Equal<string?>(null, CodexExecutableResolver.Find(Path.Combine(root, "missing", "codex.exe"), [root]));
    });
    return Task.CompletedTask;
});
Test("a shell shim alone is never returned as the native executable", () =>
{
    WithCodexInstallation((root, add) =>
    {
        var shim = add(["codex.cmd"]); add(["codex.ps1"]); add(["codex"]);
        Equal<string?>(null, CodexExecutableResolver.Find("codex", [root]));
        Equal<string?>(null, CodexExecutableResolver.Find(shim, []));
    });
    return Task.CompletedTask;
});
Test("automatic discovery skips Desktop WindowsApps aliases", () =>
{
    WithCodexInstallation((root, add) =>
    {
        add(["WindowsApps", "codex.exe"]);
        var native = add(["cli", "codex.exe"]);
        Equal(native, CodexExecutableResolver.Find("codex", [Path.Combine(root, "WindowsApps"), Path.Combine(root, "cli")]));
    });
    return Task.CompletedTask;
});
Test("automatic discovery respects a custom npm global prefix", () =>
{
    var oldPath = Environment.GetEnvironmentVariable("PATH");
    var oldPrefix = Environment.GetEnvironmentVariable("NPM_CONFIG_PREFIX");
    try
    {
        WithCodexInstallation((root, add) =>
        {
            var native = add(["node_modules", "@openai", "codex", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe"]);
            Environment.SetEnvironmentVariable("PATH", "");
            Environment.SetEnvironmentVariable("NPM_CONFIG_PREFIX", root);
            Equal(native, CodexExecutableResolver.Resolve("codex.exe"));
        });
    }
    finally
    {
        Environment.SetEnvironmentVariable("PATH", oldPath);
        Environment.SetEnvironmentVariable("NPM_CONFIG_PREFIX", oldPrefix);
    }
    return Task.CompletedTask;
});

Test("priority ordering and sequential lane", async () =>
{
    await using var r = new Rig(); await r.Connect();
    var a = await r.Add(); var b = await r.Add();
    await r.Scheduler.MoveAsync(b, -1); await r.Enable();
    Equal(1, r.Client.Starts.Count); Equal(b, r.Client.Starts[0]);
    r.Client.Complete(b); await r.WaitFor(s => s.Conversations.Single(c => c.Id == a).ExecutionActive);
    Equal(2, r.Client.Starts.Count);
});
Test("starred and sequential lanes run independently", async () =>
{
    await using var r = new Rig(); await r.Connect();
    var a = await r.Add(); var b = await r.Add(parallel: true); await r.Add(); var d = await r.Add(parallel: true);
    await r.Enable(); Equal(3, r.Client.Starts.Count); True(r.Client.Starts.Contains(a) && r.Client.Starts.Contains(b) && r.Client.Starts.Contains(d));
});
Test("same repository blocks starred concurrency", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add("/same", true); await r.Add("/same", true);
    await r.Enable(); Equal(1, r.Client.Starts.Count);
});
Test("nested workspaces block concurrency", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add("/same", true); await r.Add("/same/nested", true);
    await r.Enable(); Equal(1, r.Client.Starts.Count);
});
Test("repeated ticks never duplicate an active turn", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add(); await r.Enable();
    for (int i = 0; i < 10; i++) { r.Now = r.Now.AddSeconds(31); await r.Scheduler.TickAsync(); }
    Equal(1, r.Client.Starts.Count);
});
Test("OFF preserves active execution and stops future starts", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); await r.Add(); await r.Enable();
    await r.Scheduler.SetEnabledAsync(false); True((await r.State()).Conversations[0].ExecutionActive);
    r.Client.Complete(a); await r.WaitFor(s => s.Conversations[0].Status == ConversationStatus.Completed);
    await r.Scheduler.TickAsync(); Equal(1, r.Client.Starts.Count);
});
Test("completion disarms and records timestamp", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); await r.Enable();
    r.Client.Complete(a); await r.WaitFor(s => s.Conversations[0].Status == ConversationStatus.Completed);
    var c = (await r.State()).Conversations[0]; True(!c.Armed && c.CompletedAt != null);
    r.Now = r.Now.AddHours(5); await r.Scheduler.TickAsync(); Equal(1, r.Client.Starts.Count);
});
Test("imported completed sessions never receive a prompt", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add(status: "completed"); await r.Enable(); Equal(0, r.Client.Starts.Count);
});
Test("explicit rearm permits exactly one new turn", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(status: "completed");
    await r.Scheduler.RearmAsync(a, false); await r.Enable(); Equal(1, r.Client.Starts.Count);
    r.Client.Complete(a); await r.WaitFor(s => s.Conversations[0].Status == ConversationStatus.Completed);
    await r.Scheduler.TickAsync(); Equal(1, r.Client.Starts.Count);
});
Test("rate limits wait through reset and verify backend permission", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add();
    var reset = r.Now.AddMinutes(30); r.Client.Usage = new(false, true, reset, "Limit");
    await r.Enable(); Equal(0, r.Client.Starts.Count); Equal(ConversationStatus.RateLimited, (await r.State()).Conversations[0].Status);
    r.Now = reset.AddSeconds(-1); await r.Scheduler.TickAsync(); Equal(1, r.Client.UsageReads);
    r.Now = reset.AddSeconds(6); r.Client.Usage = new(false, false, null, "Backend has not granted ordinary usage");
    await r.Scheduler.TickAsync(); Equal(0, r.Client.Starts.Count); Equal(2, r.Client.UsageReads);
    r.Now = r.Now.AddHours(1); r.Client.Usage = new(true, false, null, "Available");
    await r.Scheduler.TickAsync(); Equal(1, r.Client.Starts.Count);
});
Test("unknown reset backs off without sending probes", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add(); r.Client.Usage = new(null, true, null, "Unknown");
    await r.Enable(); for (int i = 0; i < 14; i++) { r.Now = r.Now.AddMinutes(1); await r.Scheduler.TickAsync(); }
    Equal(1, r.Client.UsageReads); Equal(0, r.Client.Starts.Count);
    r.Now = r.Now.AddMinutes(2); await r.Scheduler.TickAsync(); Equal(2, r.Client.UsageReads);
    var c = (await r.State()).Conversations[0]; True(c.NextCheckAt >= r.Now.AddMinutes(30));
});
Test("low percentages without ordinaryUsageAllowed do not grant recovery", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add(); r.Client.Usage = new(null, false, null, "Unknown permission");
    await r.Enable(); Equal(0, r.Client.Starts.Count);
});
Test("terminal usage error automatically waits, without repeating prompts", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); await r.Enable();
    r.Client.Complete(a, "failed", "usageLimitExceeded");
    await r.WaitFor(s => s.Conversations[0].Status == ConversationStatus.RateLimited);
    for (int i = 0; i < 10; i++) await r.Scheduler.TickAsync();
    Equal(1, r.Client.Starts.Count);
});
Test("one server-side limit prevents subsequent starts that cycle", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add(parallel: true); await r.Add(parallel: true);
    r.Client.NextTurnError = "rateLimitExceeded"; await r.Enable(); Equal(1, r.Client.Starts.Count);
    await r.Scheduler.TickAsync(); Equal(1, r.Client.Starts.Count);
});
Test("multiple rate windows choose latest exhausted reset", () =>
{
    var usage = Protocol.Usage(Json("""{"ordinaryUsageAllowed":false,"rateLimits":{"primary":{"usedPercent":100,"resetsAt":1000},"secondary":{"usedPercent":100,"resetsAt":2000}}}"""));
    Equal(DateTimeOffset.FromUnixTimeSeconds(2000), usage.ResetAt!.Value); True(!usage.CanResume); return Task.CompletedTask;
});
Test("spend control remains blocked despite available credits", () =>
{
    var usage = Protocol.Usage(Json("""{"ordinaryUsageAllowed":true,"rateLimits":{"spendControlReached":true,"credits":{"hasCredits":true,"unlimited":true}}}"""));
    True(!usage.CanResume); return Task.CompletedTask;
});
Test("thread UUID mismatch is rejected before dispatch", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); r.Client.MismatchRead = true;
    await r.Enable(); Equal(0, r.Client.Starts.Count); Equal(ConversationStatus.NeedsAttention, (await r.State()).Conversations.Single(c => c.Id == a).Status);
});
Test("resume UUID mismatch is rejected before dispatch", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add(); r.Client.MismatchResume = true;
    await r.Enable(); Equal(0, r.Client.Starts.Count);
});
Test("UUID comparison accepts canonical equivalent identities", () =>
{
    var id = Guid.NewGuid(); AppServerClient.ValidateId(id.ToString().ToUpperInvariant(), id.ToString());
    return Throws(() => { AppServerClient.ValidateId(id.ToString(), Guid.NewGuid().ToString()); return Task.CompletedTask; });
});
Test("durable prepared attempt precedes network dispatch", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add();
    r.Client.BeforeStart = id => { var a = r.Store.State.Attempts.Single(); Equal(AttemptPhase.Prepared, a.Phase); Equal(id, a.ThreadId); True(a.Id.Length > 0); };
    await r.Enable(); Equal(1, r.Client.Starts.Count);
});
Test("lost response never triggers automatic duplicate", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add(); r.Client.LoseResponse = true;
    await r.Enable(); Equal(ConversationStatus.NeedsAttention, (await r.State()).Conversations[0].Status);
    for (int i = 0; i < 5; i++) { r.Now = r.Now.AddMinutes(1); await r.Scheduler.TickAsync(); }
    Equal(1, r.Client.Starts.Count); Equal(AttemptPhase.Prepared, (await r.State()).Attempts[0].Phase);
});
Test("crash before acceptance is reconciled conservatively", async () =>
{
    var store = new MemoryStore(); string id;
    await using (var before = new Rig(store))
    {
        await before.Connect(); id = await before.Add(); before.Client.LoseResponse = true; await before.Enable();
    }
    await using var after = new Rig(store);
    after.Client.Threads[id] = new(id, "Recovered", "/repo/" + id, "idle", [], new(Guid.NewGuid().ToString(), "completed"));
    await after.Connect(); await after.Enable(); Equal(0, after.Client.Starts.Count);
    Equal(ConversationStatus.NeedsAttention, (await after.State()).Conversations[0].Status);
    await Throws(() => after.Scheduler.RearmAsync(id, false));
    await after.Scheduler.RearmAsync(id, true); await after.Scheduler.TickAsync(); Equal(1, after.Client.Starts.Count);
});
Test("accepted attempt completed during restart is disarmed", async () =>
{
    var store = new MemoryStore(); ThreadSnapshot thread;
    await using (var before = new Rig(store))
    { await before.Connect(); var id = await before.Add(); await before.Enable(); thread = before.Client.Threads[id]; }
    await using var after = new Rig(store); after.Client.Threads[thread.Id] = thread with { RuntimeStatus = "idle", LastTurn = thread.LastTurn! with { Status = "completed" } };
    await after.Connect(); await after.Enable(); Equal(0, after.Client.Starts.Count);
    Equal(AttemptPhase.Completed, (await after.State()).Attempts[0].Phase);
});
Test("accepted live execution is revalidated without resending", async () =>
{
    var store = new MemoryStore(); ThreadSnapshot thread;
    await using (var before = new Rig(store)) { await before.Connect(); var id = await before.Add(); await before.Enable(); thread = before.Client.Threads[id]; }
    await using var after = new Rig(store); after.Client.Threads[thread.Id] = thread;
    await after.Connect(); await after.Enable(); Equal(0, after.Client.Starts.Count);
    Equal(ConversationStatus.Running, (await after.State()).Conversations[0].Status);
});
Test("disk failure prevents dispatch", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add(); await r.Scheduler.SetEnabledAsync(true);
    r.Client.BeforeStart = _ => throw new Exception("Should not dispatch"); r.Store.Fail = true;
    await Throws(() => r.Scheduler.TickAsync()); Equal(0, r.Client.Starts.Count);
});
Test("parallel completion updates only its own conversation", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(parallel: true); var b = await r.Add(parallel: true); await r.Enable();
    r.Client.Complete(a); await r.WaitFor(s => s.Conversations[0].Status == ConversationStatus.Completed);
    Equal(ConversationStatus.Running, (await r.State()).Conversations.Single(c => c.Id == b).Status);
});
Test("delayed completion cannot finish a newer attempt", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); await r.Enable(); var old = r.Client.Threads[a].LastTurn!.Id;
    r.Client.Complete(a); await r.WaitFor(s => s.Conversations[0].Status == ConversationStatus.Completed);
    await r.Scheduler.RearmAsync(a, false); await r.Scheduler.TickAsync();
    r.Client.Emit("turn/completed", new { threadId = a, turn = new { id = old, status = "completed" } });
    await Task.Delay(30); Equal(ConversationStatus.Running, (await r.State()).Conversations[0].Status);
});
Test("approval request is declined and latches manual attention", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); await r.Enable();
    r.Client.Emit("item/commandExecution/requestApproval", new { threadId = a }, true);
    await r.WaitFor(s => s.Conversations[0].Status == ConversationStatus.NeedsAttention); Equal(1, r.Client.Rejections);
    r.Client.Complete(a); await r.WaitFor(s => !s.Conversations[0].ExecutionActive);
    Equal(ConversationStatus.NeedsAttention, (await r.State()).Conversations[0].Status); Equal(1, r.Client.Starts.Count);
});
Test("transient error does not release active turn", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); await r.Enable();
    r.Client.Emit("error", new { threadId = a, turnId = r.Client.Threads[a].LastTurn!.Id, willRetry = true, error = new { message = "Retrying", codexErrorInfo = "rateLimitExceeded" } });
    await Task.Delay(30); True((await r.State()).Conversations[0].ExecutionActive); Equal(1, r.Client.Starts.Count);
});
Test("per-conversation pause does not pause other lanes", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); var b = await r.Add(parallel: true);
    await r.Scheduler.UpdateAsync(a, c => c.Paused = true); await r.Enable(); Equal(1, r.Client.Starts.Count); Equal(b, r.Client.Starts[0]);
});
Test("running execution lane cannot be changed", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); await r.Enable();
    await Throws(() => r.Scheduler.UpdateAsync(a, c => c.Parallel = true)); True(!(await r.State()).Conversations[0].Parallel);
});
Test("duplicate session and sixth entry are rejected", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); await Throws(() => r.Scheduler.AddAsync(a));
    for (int i = 0; i < 4; i++) await r.Add(); await Throws(() => r.Add()); Equal(5, (await r.State()).Conversations.Count);
});
Test("workspace drift prevents continuation", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); r.Client.Threads[a] = r.Client.Threads[a] with { Workspace = "/other" };
    await r.Enable(); Equal(0, r.Client.Starts.Count);
});
Test("disconnect stops scheduler without discarding journal", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add(); await r.Enable(); r.Client.Disconnect();
    await r.WaitFor(s => !s.Enabled); Equal(1, (await r.State()).Attempts.Count); Equal(1, r.Client.Starts.Count);
});
Test("ownership loss blocks all new work", async () =>
{
    await using var r = new Rig(); await r.Connect(); await r.Add(); r.Owned = false;
    await r.Scheduler.SetEnabledAsync(false); await r.Scheduler.TickAsync(); await Throws(() => r.Scheduler.SetEnabledAsync(true)); Equal(0, r.Client.Starts.Count);
});
Test("unknown runtime status fails closed", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); r.Client.Threads[a] = r.Client.Threads[a] with { RuntimeStatus = "future-status" };
    await r.Enable(); Equal(0, r.Client.Starts.Count);
});
Test("history with unfinished unloaded execution is not resumed", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); r.Client.Threads[a] = r.Client.Threads[a] with { RuntimeStatus = "notLoaded", LastTurn = new("turn", "inProgress") };
    await r.Enable(); Equal(0, r.Client.Starts.Count); Equal(ConversationStatus.NeedsAttention, (await r.State()).Conversations[0].Status);
});
Test("atomic JSON persistence keeps previous backup and journal", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "autoresume-test-" + Guid.NewGuid());
    try
    {
        var path = Path.Combine(dir, "state.json"); var store = new JsonStateStore(path); var state = new AppState();
        var attempt = new ResumeAttempt { ThreadId = Guid.NewGuid().ToString(), Phase = AttemptPhase.Prepared };
        state.Attempts.Add(attempt);
        state.Conversations.Add(new() { Id = attempt.ThreadId, Workspace = "/repo", AttemptId = attempt.Id }); store.Save(state);
        state.Enabled = true; store.Save(state); True(store.Load().Enabled); Equal(1, store.Load().Attempts.Count);
        True(File.Exists(path + ".bak")); True(!File.Exists(path + ".tmp"));
        File.WriteAllText(path, "{corrupt");
        return Throws(() => { store.Load(); return Task.CompletedTask; });
    }
    finally { Directory.Delete(dir, true); }
});
Test("structured HTTP 429 is recognized without message scraping", () =>
{
    Equal("rateLimitExceeded", Protocol.ErrorCode(Json("""{"responseStreamDisconnected":{"httpStatusCode":429}}""")));
    True(!Protocol.IsLimit("other")); return Task.CompletedTask;
});

Test("real subprocess RPC framing, pagination, identity, errors and approval denial", async () =>
{
    await using var client = new AppServerClient();
    var request = new TaskCompletionSource<CodexEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
    var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    client.EventReceived += e => { if (e.RequestId != null) request.TrySetResult(e); if (e.Method == "fixture/rejected") rejected.TrySetResult(); };
    await client.ConnectAsync(Environment.ProcessPath!, CancellationToken.None);
    Equal(1, (await client.ListAsync(CancellationToken.None)).Count);
    Equal("old-turn", (await client.ReadAsync(ProtocolFixture.Id, CancellationToken.None)).LastTurn!.Id);
    Equal(ProtocolFixture.Id, (await client.ResumeAsync(ProtocolFixture.Id, CancellationToken.None)).Id);
    await Throws(() => client.ReadAsync(Guid.NewGuid().ToString(), CancellationToken.None));
    True((await client.ReadUsageAsync(CancellationToken.None)).CanResume);
    Equal("new-turn", (await client.StartTurnAsync(ProtocolFixture.Id, "fixture prompt", "attempt-fixture", CancellationToken.None)).Id);
    await client.RejectRequestAsync(await request.Task.WaitAsync(TimeSpan.FromSeconds(10)), CancellationToken.None);
    await rejected.Task.WaitAsync(TimeSpan.FromSeconds(10));
});

Test("duplicate historical completion cannot undo explicit rearm", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(status: "completed");
    await r.Scheduler.RearmAsync(a, false);
    r.Client.Complete(a); await Task.Delay(30);
    True((await r.State()).Conversations[0].Armed);
    await r.Enable(); Equal(1, r.Client.Starts.Count);
});

Test("polled external activity blocks rather than automatically continuing", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add();
    r.Client.Threads[a] = r.Client.Threads[a] with { LastTurn = new("external-turn", "interrupted") };
    await r.Enable(); Equal(0, r.Client.Starts.Count); Equal(ConversationStatus.NeedsAttention, (await r.State()).Conversations[0].Status);
});

Test("Git common directory identifies shared worktrees", async () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "autoresume-git-" + Guid.NewGuid());
    Directory.CreateDirectory(dir);
    async Task Git(params string[] arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var p = System.Diagnostics.Process.Start(info)!;
        var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(); await output;
        if (p.ExitCode != 0) throw new Exception(await error);
    }
    try
    {
        await Git("init", "-b", "main");
        await Git("-c", "user.name=AutoResume Test", "-c", "user.email=test@example.invalid", "commit", "--allow-empty", "-m", "fixture");
        var worktree = Path.Combine(dir, "other");
        await Git("worktree", "add", "-b", "other", worktree);
        var original = await WorkspaceIdentity.GetAsync(dir, CancellationToken.None);
        Equal(original, await WorkspaceIdentity.GetAsync(worktree, CancellationToken.None));
        Directory.CreateDirectory(Path.Combine(dir, "nested"));
        Equal(original, await WorkspaceIdentity.GetAsync(Path.Combine(dir, "nested"), CancellationToken.None));
    }
    finally
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }
});

Test("one conversation's failed event reconciliation leaves other executions running", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(parallel: true); var b = await r.Add(parallel: true); await r.Enable();
    r.Client.ReadFailureId = a;
    r.Client.Emit("thread/status/changed", new { threadId = a, status = new { type = "idle" } });
    await r.WaitFor(s => s.Conversations[0].Status == ConversationStatus.NeedsAttention);
    var state = await r.State(); True(state.Enabled); Equal(ConversationStatus.Running, state.Conversations.Single(c => c.Id == b).Status);
});

Test("rate notifications supply reset estimates without granting recovery", async () =>
{
    await using var r = new Rig(); await r.Connect(); var a = await r.Add(); await r.Enable();
    var reset = r.Now.AddHours(2);
    r.Client.Emit("account/rateLimits/updated", new { rateLimits = new { primary = new { usedPercent = 100, resetsAt = reset.ToUnixTimeSeconds() } } });
    r.Client.Complete(a, "failed", "usageLimitExceeded");
    await r.WaitFor(s => s.Conversations[0].Status == ConversationStatus.RateLimited);
    var row = (await r.State()).Conversations[0]; Equal(reset, row.ResetAt!.Value); Equal(reset.AddSeconds(5), row.NextCheckAt!.Value);
    await r.Scheduler.TickAsync(); Equal(1, r.Client.Starts.Count);
    r.Client.Usage = new(false, false, null, "Permission still denied");
    r.Client.Emit("account/rateLimits/updated", new { rateLimits = new { primary = new { usedPercent = 10 } } });
    await r.WaitFor(s => s.Conversations[0].NextCheckAt == r.Now.AddSeconds(30));
    await r.Scheduler.TickAsync(); Equal(1, r.Client.UsageReads);
    r.Now = r.Now.AddSeconds(31); await r.Scheduler.TickAsync();
    Equal(2, r.Client.UsageReads); Equal(1, r.Client.Starts.Count);
});

async Task ProbeFixture(string? mode, Func<AppServerClient, System.Collections.Concurrent.ConcurrentQueue<string>, Task> run)
{
    var previous = Environment.GetEnvironmentVariable("AUTORESUME_PROBE_FIXTURE");
    await using var client = new AppServerClient();
    var methods = new System.Collections.Concurrent.ConcurrentQueue<string>();
    client.EventReceived += e => { if (e.Method == "fixture/method") methods.Enqueue(Protocol.String(e.Parameters, "method")!); };
    try
    {
        Environment.SetEnvironmentVariable("AUTORESUME_PROBE_FIXTURE", mode);
        await client.ConnectAsync(Environment.ProcessPath!, CancellationToken.None);
    }
    finally { Environment.SetEnvironmentVariable("AUTORESUME_PROBE_FIXTURE", previous); }
    await run(client, methods);
    True(methods.All(method => method is "initialize" or "thread/read" or "thread/turns/list" or "thread/items/list" or "thread/resume"));
}

Test("continuity probe retains existing UUID and full recorded context without a turn", () => ProbeFixture(null, async (client, methods) =>
{
    var result = await client.VerifyExistingThreadAsync(ProtocolFixture.Id, () => { });
    Equal(ProtocolFixture.Id, result.ResumedId); Equal(1, result.Turns); Equal(1, result.Items);
    Equal(result.HistoryBeforeSha256, result.HistoryAfterSha256); Equal(64, result.HistoryBeforeSha256.Length);
    Equal(1, methods.Count(method => method == "thread/resume"));
}));
Test("continuity probe checks every page of history", () => ProbeFixture("pagination", async (client, _) =>
{
    var result = await client.VerifyExistingThreadAsync(ProtocolFixture.Id, () => { });
    Equal(2, result.Turns); Equal(2, result.Items);
}));
Test("unknown existing UUID fails without a resume or creation fallback", () => ProbeFixture(null, async (client, methods) =>
{
    await Throws(() => client.VerifyExistingThreadAsync(Guid.NewGuid().ToString(), () => { }));
    True(!methods.Contains("thread/resume"));
}));
Test("active thread is never probed through resume", () => ProbeFixture("active", async (client, methods) =>
{
    await Throws(() => client.VerifyExistingThreadAsync(ProtocolFixture.Id, () => { }));
    True(!methods.Contains("thread/resume"));
}));
Test("unfinished stored execution blocks continuity verification", () => ProbeFixture("unfinished", async (client, methods) =>
{
    await Throws(() => client.VerifyExistingThreadAsync(ProtocolFixture.Id, () => { }));
    True(!methods.Contains("thread/resume"));
}));
Test("empty context does not count as successful Desktop continuity", () => ProbeFixture("empty-context", async (client, methods) =>
{
    await Throws(() => client.VerifyExistingThreadAsync(ProtocolFixture.Id, () => { }));
    True(!methods.Contains("thread/resume"));
}));
Test("changed resume UUID fails without creating a replacement", () => ProbeFixture("uuid-change", async (client, methods) =>
{
    await Throws(() => client.VerifyExistingThreadAsync(ProtocolFixture.Id, () => { }));
    Equal(1, methods.Count(method => method == "thread/resume"));
}));
Test("changed context makes the continuity probe fail", () => ProbeFixture("context-change", async (client, _) =>
{
    await Throws(() => client.VerifyExistingThreadAsync(ProtocolFixture.Id, () => { }));
}));
Test("changed workspace makes the continuity probe fail", () => ProbeFixture("workspace-change", async (client, _) =>
{
    await Throws(() => client.VerifyExistingThreadAsync(ProtocolFixture.Id, () => { }));
}));
Test("loss of exclusivity aborts before the next continuity operation", () => ProbeFixture(null, async (client, methods) =>
{
    int checks = 0;
    await Throws(() => client.VerifyExistingThreadAsync(ProtocolFixture.Id, () => { if (++checks > 4) throw new InvalidOperationException("Exclusive access lost"); }));
    True(!methods.Contains("thread/resume"));
}));

int failed = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + "\n" + ex); }
}
Console.WriteLine($"{tests.Count - failed} passed, {failed} failed, {tests.Count} executed.");
return failed == 0 ? 0 : 1;
