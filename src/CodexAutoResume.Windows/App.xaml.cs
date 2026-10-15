using System.Diagnostics;
using System.Windows;
using CodexAutoResume.Core;

namespace CodexAutoResume.Windows;

public partial class App : Application
{
    private Mutex? instance;
    private FileStream? stateLock;
    private string? smokeDirectory;
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexAutoResume");
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var selfTest = e.Args.Contains("--self-test");
        instance = new Mutex(true, selfTest ? @"Local\CodexAutoResume.Smoke." + Guid.NewGuid() : @"Local\CodexAutoResume.Instance", out var created);
        if (!created) { MessageBox.Show("Codex AutoResume is already running. Open it from the system tray."); Shutdown(); return; }
        try
        {
            var directory = selfTest ? smokeDirectory = Path.Combine(Path.GetTempPath(), "CodexAutoResume-smoke-" + Guid.NewGuid()) : DataDirectory;
            Directory.CreateDirectory(directory);
            // Also protects the same user's data across distinct Windows/RDP sessions.
            stateLock = new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var client = new AppServerClient();
            OwnedServerId = () => client.ProcessId;
            var scheduler = new Scheduler(client, new JsonStateStore(Path.Combine(directory, "state.json")), ownershipCheck: OtherCodexClientsAbsent);
            var window = new MainWindow(scheduler, client);
            MainWindow = window;
            if (selfTest)
                window.Loaded += async (_, _) => await window.SmokeCloseAsync();
            window.Show();
            if (e.Args.Contains("--tray")) window.Hide();
        }
        catch (Exception ex)
        {
            MessageBox.Show("AutoResume could not safely load its state. No work was scheduled.\n\n" + ex.Message +
                "\n\nState and backup are in: " + DataDirectory, "Codex AutoResume", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static bool OtherCodexClientsAbsent()
    {
        // Best-effort extra guard, not a cross-client lock. Ownership is an explicit
        // user contract because the protocol does not offer a global Desktop lease.
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    var name = p.ProcessName;
                    if (name.Equals("Codex", StringComparison.OrdinalIgnoreCase) &&
                        !OwnedServer(p.Id)) return false;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { return false; }
            }
        }
        return true;
    }
    private static bool OwnedServer(int id) => OwnedServerId?.Invoke() == id;
    private static Func<int?>? OwnedServerId { get; set; }

    protected override void OnExit(ExitEventArgs e)
    {
        stateLock?.Dispose(); instance?.Dispose();
        if (smokeDirectory != null) Directory.Delete(smokeDirectory, true);
        base.OnExit(e);
    }
}
