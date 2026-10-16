using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexAutoResume.Core;

namespace CodexAutoResume.Windows;

public partial class MainWindow
{
    // These snapshots exercise presentation only. They never reach the scheduler.
    internal async Task ValidateUiAsync(string? fixtureDirectory, string outputDirectory)
    {
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        Directory.CreateDirectory(outputDirectory);
        async Task Capture(string name)
        {
            await Task.Delay(180); UpdateLayout();
            var image = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(outputDirectory, name + ".png")); encoder.Save(stream);
        }
        Assert(!scheduler.IsConnected && WatcherHeading.Text == "Watcher disconnected", "Disconnected watcher was shown as healthy.");
        await Capture("01-empty");
        AccentHex.Text = "#55DDFF";
        Assert(appearance.Accent == "#55DDFF", "Accent preview failed.");
        AccentHex.Text = "invalid";
        Assert(appearance.Accent == "#55DDFF" && AccentValidation.Text.Length > 0, "Invalid HEX replaced the active accent.");
        AccentHex.Text = "#55DDFF"; ReduceMotion.IsChecked = true; ReduceGlow.IsChecked = true;
        AppearanceChanged(this, new RoutedEventArgs());
        Assert(!MotionEnabled && TerminalMark.Effect == null, "Reduced motion/glow settings were ignored.");
        await FlushAppearanceAsync();
        Assert(scheduler.Snapshot.Appearance.Accent == "#55DDFF", "Appearance did not persist in scheduler state.");
        await Capture("02-cyan-reduced-motion");
        ResetAppearanceClick(this, new RoutedEventArgs());
        RecordError("Test fixture: Codex executable is missing. Re-detect or Browse, then Connect to retry.");
        Assert(ConnectionErrorPanel.Visibility == Visibility.Visible && WatcherHeading.Text == "Connection needs attention", "Connection failure was hidden.");
        await Capture("03-connection-error"); connectionFailure = null;
        if (fixtureDirectory != null)
        {
            var oldPath = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("PATH", fixtureDirectory + Path.PathSeparator + oldPath);
                SetExecutable("codex.exe"); await FindCodexAsync(showMissing: false);
                Assert(DetectedVersion.Text.StartsWith("codex-cli"), "Automatic startup discovery did not validate the CLI.");
                Exclusive.IsChecked = true; await ConnectAsync();
                Assert(scheduler.IsConnected && client.ProcessId != null, ConnectionError.Text.Length > 0 ? ConnectionError.Text : "Discovered CLI did not connect.");
                Assert((await scheduler.DiscoverAsync()).Count == 1, "Owned test app-server discovery failed.");
                Assert(!state.Enabled && WatcherHeading.Text.Contains("OFF"), "Connecting must not silently turn scheduling ON.");
                await Capture("04-connected-off");
            }
            finally { Environment.SetEnvironmentVariable("PATH", oldPath); }
        }
        await scheduler.SetEnabledAsync(false);
        await FlushAppearanceAsync();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        var sample = new AppState { Enabled = true, Conversations = Enum.GetValues<ConversationStatus>().Take(5).Select((status, i) => new Conversation {
            Id = Guid.NewGuid().ToString(), Name = new[] { "Implement executable discovery", "Finish integration checks", "Wait for usage verification", "Update installation guide", "Resolve required approval" }[i],
            Workspace = @"C:\Projects\Codex AutoResume", Revalidated = true, Status = status,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-12), CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
            WaitingForUsage = status == ConversationStatus.RateLimited, Reason = status == ConversationStatus.RateLimited ? "Reset unavailable · conservative backoff" : "Presentation fixture · no model turns submitted",
            Attention = status == ConversationStatus.NeedsAttention }) .ToList() };
        Render(sample); await Capture("05-queue-statuses");
        Width = 640; await Capture("06-narrow");
        Assert(rows.Count == 5 && rows.Any(r => r.StatusLabel == "Rate Limited") && rows.Any(r => r.StatusLabel == "Needs Attention"), "Queue status presentation failed.");
        Render(scheduler.Snapshot);
        Console.WriteLine("PASS WPF theme preview, invalid HEX, reduced motion, failure visibility, native discovery/connection and narrow layout.");
    }
}
