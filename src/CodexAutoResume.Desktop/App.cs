using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using CodexAutoResume.Core;

namespace CodexAutoResume.Desktop;

public sealed class App : Application
{
    private FileStream? stateLock;
    private string? testDirectory;
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var directory = Program.SelfTest ? testDirectory = Path.Combine(Path.GetTempPath(), "autoresume-desktop-test-" + Guid.NewGuid()) : DesktopIntegration.DataDirectory;
                Directory.CreateDirectory(directory);
                stateLock = new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var client = new AppServerClient();
                var scheduler = new Scheduler(client, new JsonStateStore(Path.Combine(directory, "state.json")),
                    ownershipCheck: () => DesktopIntegration.OtherCodexClientsAbsent(client.ProcessId));
                desktop.MainWindow = new MainWindow(scheduler, client);
                desktop.Exit += (_, _) =>
                {
                    stateLock?.Dispose();
                    if (testDirectory != null) Directory.Delete(testDirectory, true);
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                var message = new Window { Title = "Codex AutoResume", Width = 620, Height = 210,
                    Content = new TextBlock { Text = "Could not safely open the saved queue. Another instance may be running. No work was scheduled.\n\n" + ex.Message, Margin = new Thickness(24), TextWrapping = Avalonia.Media.TextWrapping.Wrap } };
                message.Closed += (_, _) => desktop.Shutdown(1);
                desktop.MainWindow = message;
                if (Program.SelfTest) desktop.Shutdown(1);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
