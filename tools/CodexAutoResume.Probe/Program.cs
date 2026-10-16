using System.Diagnostics;
using System.Text.Json;
using CodexAutoResume.Core;

// Diagnostic only. It has no scheduler and never invokes StartTurnAsync.
var ids = new List<string>();
string executable = "codex.exe", output = "desktop-continuity.json";
bool confirmedClosed = false;
try
{
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--codex": executable = args[++i]; break;
            case "--output": output = args[++i]; break;
            case "--desktop-closed": confirmedClosed = true; break;
            default:
                if (!Guid.TryParse(args[i], out var id)) throw new ArgumentException("Each selected conversation must be an exact existing UUID.");
                ids.Add(id.ToString()); break;
        }
    }
    if (!confirmedClosed || ids.Count is < 1 or > 5 || ids.Distinct().Count() != ids.Count)
        throw new ArgumentException("Finish/resolve active Desktop work, close Desktop and AutoResume, then pass --desktop-closed and one to five distinct Desktop UUIDs.");
}
catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidOperationException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Usage: CodexAutoResume.Probe[.exe] --desktop-closed UUID [UUID...] [--codex PATH] [--output desktop-continuity.json]");
    return 2;
}

var fullOutput = Path.GetFullPath(output);
if (File.Exists(fullOutput)) { Console.Error.WriteLine("Choose a new output path; existing reports are never overwritten."); return 2; }

await using var client = new AppServerClient();
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
client.EventReceived += e => { if (e.RequestId != null) cancellation.Cancel(); }; // Never answer an approval or login request.

void RequireExclusive()
{
    // Extra guard, not a global lease. Desktop must remain closed throughout.
    foreach (var process in Process.GetProcesses())
    {
        using (process)
        {
            if (process.Id == Environment.ProcessId || process.Id == client.ProcessId) continue;
            try
            {
                if (process.ProcessName.StartsWith("codex", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Another Codex/AutoResume process is running. Close it before validating an exclusive transfer.");
            }
            catch (System.ComponentModel.Win32Exception)
            { throw new InvalidOperationException("Process ownership cannot be checked; diagnostic stopped."); }
            catch (InvalidOperationException) when (process.HasExited) { }
        }
    }
}

var results = new List<object>();
bool allPassed = true;
try
{
    RequireExclusive();
    await client.ConnectAsync(executable, cancellation.Token);
    foreach (var id in ids)
    {
        try
        {
            var evidence = await client.VerifyExistingThreadAsync(id, RequireExclusive, cancellation.Token);
            results.Add(new { requestedId = id, outcome = "backend-history-preserved", evidence });
            Console.WriteLine($"Verified existing UUID {id}: {evidence.Turns} turns, {evidence.Items} items, identical history before/after resume. No turn submitted.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            allPassed = false;
            // No raw protocol errors or conversation contents are written to the report.
            var reason = ex switch
            {
                RpcException => "A required Codex operation rejected the selected UUID or is unavailable. No creation/fork fallback was attempted.",
                OperationCanceledException => "Diagnostic canceled, timed out, or received a server request requiring intervention.",
                InvalidDataException or InvalidOperationException => ex.Message,
                _ => "The local diagnostic could not complete. Check Codex installation and session availability."
            };
            results.Add(new { requestedId = id, outcome = "not-verified", reason });
            Console.WriteLine($"UUID {id}: NOT VERIFIED. {reason}");
        }
    }
}
catch (Exception ex) when (ex is not OutOfMemoryException)
{
    allPassed = false;
    Console.Error.WriteLine(ex is InvalidOperationException ? ex.Message : "Cannot initialize the selected Codex app-server. No turns submitted.");
    results.Add(new { outcome = "connection-not-verified" });
}

var report = new
{
    schemaVersion = 1, timestamp = DateTimeOffset.UtcNow, codexVersion = client.Version,
    backendContinuity = allPassed ? "verified-for-selected-uuids" : "not-verified",
    desktopOrigin = "operator-identified; no independent Desktop UI verification",
    desktopUiSynchronization = "unverified; not implied by backend continuity",
    activeDesktopCoordination = "no cross-process lease verified; exclusive ownership required",
    promptsSubmitted = 0, replacementThreadsRequested = 0, sessions = results
};
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
    await using var stream = new FileStream(fullOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    await JsonSerializer.SerializeAsync(stream, report, StateJson.Options);
    Console.WriteLine("Report: " + fullOutput);
}
catch (IOException) { Console.Error.WriteLine("Report could not be saved. Do not treat an unsaved check as verified."); return 2; }
return allPassed ? 0 : 1;
