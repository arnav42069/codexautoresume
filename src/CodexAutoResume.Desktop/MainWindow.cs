using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CodexAutoResume.Core;

namespace CodexAutoResume.Desktop;

public sealed class MainWindow : Window
{
    private readonly Scheduler scheduler;
    private readonly AppServerClient client;
    private readonly StackPanel cards = new() { Spacing = 10 };
    private readonly List<(ConversationView View, TextBlock Clock, Border Badge)> clocks = [];
    private readonly TextBox path = new() { MinWidth = 220, Watermark = "Codex CLI is detected automatically" };
    private readonly CheckBox exclusive = new() { Content = new TextBlock { Text = "I grant AutoResume exclusive ownership of these sessions. For execution only: other Codex clients are closed.", TextWrapping = TextWrapping.Wrap } };
    private readonly TextBlock connection = Text("Disconnected · scheduling is OFF", "#B0C3DD");
    private readonly TextBlock summary = Text("");
    private readonly TextBlock usage = Text("Usage is verified before scheduling.", "#A6BDD9");
    private readonly TextBox activity = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 140 };
    private readonly Button toggle = new() { Content = "○ OFF", MinWidth = 100 };
    private readonly CheckBox startup = new() { Content = "Start at login" };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TrayIcon? tray;
    private AppState state;
    private bool busy, exiting, pulse;

    public MainWindow(Scheduler scheduler, AppServerClient client)
    {
        this.scheduler = scheduler; this.client = client; state = scheduler.Snapshot;
        Title = "Codex AutoResume"; Width = 1000; Height = 850; MinWidth = 780; MinHeight = 620;
        Background = Brush.Parse("#101824");
        var iconPath = Path.Combine(AppContext.BaseDirectory, "icon.png");
        if (File.Exists(iconPath)) Icon = new WindowIcon(iconPath);
        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(toggle, Dock.Right); header.Children.Add(toggle);
        header.Children.Add(new StackPanel { Spacing = 5, Children = {
            new TextBlock { Text = "Codex AutoResume", FontSize = 26, FontWeight = FontWeight.SemiBold },
            Text("Your existing sessions. A durable queue. No repeated prompts.", "#A6BDD9") } });
        var tools = new DockPanel { LastChildFill = true };
        foreach (var button in new[] { Button("Connect read-only", Connect), Button("Browse…", Browse), Button("Find Codex", Find) })
        { DockPanel.SetDock(button, Dock.Right); tools.Children.Add(button); }
        tools.Children.Add(path);
        var connectPanel = new StackPanel { Spacing = 12, Children = {
            Text("Desktop may remain open for browsing. For execution ownership, finish active work and close other clients. Desktop UI synchronization is unsupported.", "#F3C77A"),
            tools, exclusive, new WrapPanel { Children = { Button("Acquire execution ownership", Acquire), Button("Refresh chats", Refresh) } }, connection } };
        var account = new Border { Background = Brush.Parse("#1B293C"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = connectPanel };
        var queueHeader = new DockPanel();
        var add = Button("+ Add conversation", Add); DockPanel.SetDock(add, Dock.Right); queueHeader.Children.Add(add); queueHeader.Children.Add(summary);
        var middle = new StackPanel { Spacing = 8, Children = { queueHeader, usage } };
        var content = new StackPanel { Spacing = 16, Children = { cards,
            new Expander { Header = "Recent activity", IsExpanded = true, Content = activity,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch } } };
        var footer = new WrapPanel { Orientation = Orientation.Horizontal };
        footer.Children.Add(startup); footer.Children.Add(Button("Desktop shortcut", Shortcut));
        footer.Children.Add(Button(OperatingSystem.IsMacOS() ? "Hide to menu bar" : "Minimize", () =>
        { if (OperatingSystem.IsMacOS() && tray != null) Hide(); else WindowState = WindowState.Minimized; return Task.CompletedTask; }));
        footer.Children.Add(Button("Exit", () => Exit(false)));
        var root = new Grid { Margin = new Thickness(22), RowDefinitions = RowDefinitions.Parse("Auto,Auto,Auto,*,Auto"), RowSpacing = 16 };
        Control[] sections = [header, account, middle, new ScrollViewer { Content = content }, footer];
        for (int i = 0; i < sections.Length; i++) { Grid.SetRow(sections[i], i); root.Children.Add(sections[i]); }
        Content = root;
        path.Text = CodexExecutableResolver.IsAutomatic(state.CodexPath) ? "codex" : state.CodexPath;
        toggle.Click += async (_, _) => await Run(async () => { await scheduler.SetEnabledAsync(!state.Enabled); await scheduler.TickAsync(); });
        startup.Click += async (_, _) => await Run(async () =>
        {
            DesktopIntegration.SetStartAtLogin(startup.IsChecked == true, Environment.ProcessPath!);
            await scheduler.SaveSettingsAsync(startup.IsChecked == true);
        });
        scheduler.Changed += next => Dispatcher.UIThread.Post(() => Render(next));
        timer.Tick += (_, _) =>
        {
            pulse = !pulse;
            foreach (var row in clocks) { row.Clock.Text = row.View.Timing; row.Badge.Opacity = row.View.IsRunning && pulse ? 0.7 : 1; }
        };
        timer.Start(); Render(state);
        if (OperatingSystem.IsMacOS() && Icon != null && !Program.SelfTest)
        {
            var menu = new NativeMenu();
            var open = new NativeMenuItem("Open Codex AutoResume"); open.Click += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
            var quit = new NativeMenuItem("Exit"); quit.Click += async (_, _) => await Run(() => Exit(false));
            menu.Items.Add(open); menu.Items.Add(quit);
            tray = new TrayIcon { Icon = Icon, ToolTipText = "Codex AutoResume", Menu = menu, IsVisible = true };
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });
        }
        Closing += async (_, e) => { if (!exiting) { e.Cancel = true; await Run(() => Exit(false)); } };
        Opened += async (_, _) =>
        {
            if (!Program.SelfTest)
            {
                try { DesktopIntegration.CreateShortcut(Environment.ProcessPath!); }
                catch (Exception ex) { connection.Text = "Desktop shortcut: " + ex.Message; }
                await Run(Connect);
                if (Program.StartInTray) { if (OperatingSystem.IsMacOS()) Hide(); else WindowState = WindowState.Minimized; }
            }
            if (Program.SelfTest && Program.FixtureDirectory != null)
            {
                var originalPath = Environment.GetEnvironmentVariable("PATH");
                try
                {
                    Environment.SetEnvironmentVariable("PATH", Program.FixtureDirectory + Path.PathSeparator + originalPath);
                    await Connect();
                    var observed = await scheduler.GetSnapshotAsync();
                    if (!scheduler.IsConnected || scheduler.HasExecutionOwnership || observed.Enabled || observed.Conversations.Count == 0 || observed.Conversations.Any(c => c.Armed || !c.ObservationOnly))
                        throw new InvalidOperationException("Native read-only automatic connection/queue import failed.");
                    Console.WriteLine("PASS native automatic read-only connection and unarmed recent queue.");
                }
                catch (Exception ex)
                { await FailSelfTest(ex); return; }
                finally { Environment.SetEnvironmentVariable("PATH", originalPath); }
            }
            if (Program.SelfTest || Program.Screenshot != null)
            {
                await Task.Delay(600);
                try
                {
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
                    bitmap.Render(this);
                    if (Program.Screenshot != null) bitmap.Save(Program.Screenshot);
                    if (Program.SelfTest) { Console.WriteLine("PASS native desktop window rendered; scheduling remains OFF."); await Exit(true); }
                }
                catch (Exception ex) { await FailSelfTest(ex); }
            }
        };
    }

    private async Task FailSelfTest(Exception error)
    {
        Console.Error.WriteLine(error); exiting = true; timer.Stop();
        await scheduler.DisposeAsync(); tray?.Dispose();
        ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(1);
    }

    private static TextBlock Text(string value, string color = "#E8EDF6") => new() { Text = value, Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private Button Button(string title, Func<Task> action)
    {
        var button = new Button { Content = title, Margin = new Thickness(3), Padding = new Thickness(12, 7) };
        button.Click += async (_, _) => await Run(action); return button;
    }
    private async Task Run(Func<Task> action)
    {
        if (busy || exiting) return; busy = true;
        try { await action(); }
        catch (Exception ex) { connection.Text = ex.Message; }
        finally { busy = false; }
    }
    private async Task Find()
    {
        if (client.ProcessId != null) { connection.Text = "Restart AutoResume to choose another Codex installation."; return; }
        var found = await CodexDiscovery.DiscoverAsync(); path.Text = found.Path;
        await scheduler.SaveExecutableAsync(found.Path);
        connection.Text = $"{found.Version} detected. Connect read-only; Desktop may stay open.";
    }
    private async Task Browse()
    {
        if (client.ProcessId != null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select the Codex CLI executable", AllowMultiple = false });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } selected)
        {
            var found = await CodexDiscovery.DiscoverAsync(selected, manualOverride: true);
            path.Text = found.Path; await scheduler.SaveExecutableAsync(found.Path);
            connection.Text = $"{found.Version} selected. Connect read-only; Desktop may stay open.";
        }
    }
    private async Task Connect()
    {
        if (scheduler.IsConnected) { connection.Text = "Already connected. Use Refresh chats to update discovery."; return; }
        if (client.ProcessId != null) { connection.Text = "Restart AutoResume to retry the existing server."; return; }
        path.IsEnabled = false;
        try
        {
            var found = await CodexDiscovery.DiscoverAsync(path.Text);
            path.Text = found.Path; connection.Text = "Connecting read-only and discovering recent chats…";
            await scheduler.ConnectForDiscoveryAsync(found.Path);
            connection.Text = $"Connected read-only · {found.Version} · Desktop may remain open";
        }
        finally { path.IsEnabled = client.ProcessId == null; }
    }
    private async Task Acquire()
    {
        await scheduler.AcquireExecutionOwnershipAsync(exclusive.IsChecked == true);
        connection.Text = "Execution ownership acquired. Review the queue, then switch ON.";
    }
    private Task Refresh() => scheduler.RefreshDiscoveryAsync();
    private async Task Add()
    {
        var id = await Dialogs.Pick(this, await scheduler.DiscoverAsync()); if (id == null) return;
        var thread = await scheduler.InspectAsync(id);
        if (await Dialogs.Confirm(this, "Verify conversation identity", $"{thread.Name}\n\nUUID: {thread.Id}\nWorkspace: {thread.Workspace}\nLatest turn: {thread.LastTurn?.Status ?? "none"}\n\nConfirm this is the original session you intended. Completed work remains disarmed."))
            await scheduler.AddAsync(id);
    }
    private Task Shortcut() { connection.Text = DesktopIntegration.CreateShortcut(Environment.ProcessPath!, force: true); return Task.CompletedTask; }
    private async Task Exit(bool test)
    {
        await scheduler.SetEnabledAsync(false);
        var current = await scheduler.GetSnapshotAsync();
        if (!test && (current.Conversations.Any(c => c.ExecutionActive) || current.Attempts.Any(a => a.Phase is AttemptPhase.Prepared or AttemptPhase.Accepted)))
        { connection.Text = "Scheduling is OFF. Active or uncertain attempts remain; finish or reconcile them before exiting."; return; }
        exiting = true; timer.Stop(); await scheduler.DisposeAsync(); tray?.Dispose();
        ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(0);
    }

    private void Render(AppState next)
    {
        if (exiting) return; state = next;
        toggle.Content = next.Enabled ? "● ON" : "○ OFF"; startup.IsChecked = next.StartWithWindows;
        int Count(ConversationStatus status) => next.Conversations.Count(c => !c.ObservationOnly && c.Status == status || status == ConversationStatus.Running && c.ObservationOnly && c.ObservedActive);
        summary.Text = $"{Count(ConversationStatus.Running)} running · {Count(ConversationStatus.Queued)} queued · {Count(ConversationStatus.RateLimited)} rate limited · {Count(ConversationStatus.Completed)} completed";
        if (next.Conversations.Any(c => !c.ObservationOnly && !c.Revalidated)) summary.Text = "Last known: " + summary.Text;
        activity.Text = string.Join(Environment.NewLine, next.Log);
        usage.Text = !scheduler.HasExecutionOwnership ? (next.DiscoveryError ?? next.DiscoveryOrder) : next.Conversations.Any(c => c.WaitingForUsage) ? "Usage limited · reset estimates below are verified with Codex before resuming." : "Usage is checked before scheduling. Completed work stays completed.";
        cards.Children.Clear(); clocks.Clear();
        if (next.Conversations.Count == 0)
            cards.Children.Add(new Border { Padding = new Thickness(22), Child = Text("Your five recent chats appear automatically in an empty queue. Existing priorities stay fixed. Desktop may remain open while browsing.", "#A6BDD9") });
        var queued = next.Conversations.Where(c => c.Status == ConversationStatus.Queued).Select(c => c.Id).ToList();
        for (int i = 0; i < next.Conversations.Count; i++)
        {
            var item = next.Conversations[i]; var view = new ConversationView(item); view.Update(item, i + 1, queued.IndexOf(item.Id) + 1);
            var badge = new Border { CornerRadius = new CornerRadius(8), Background = Brush.Parse(view.BadgeBrush), Padding = new Thickness(10, 5), Child = Text(view.StatusLabel) };
            var heading = new DockPanel(); DockPanel.SetDock(badge, Dock.Right); heading.Children.Add(badge);
            heading.Children.Add(new TextBlock { Text = view.Heading, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            var timing = Text(view.Timing, "#AED4FF"); clocks.Add((view, timing, badge));
            var actions = new WrapPanel();
            actions.Children.Add(Button("↑", () => scheduler.MoveAsync(item.Id, -1)));
            actions.Children.Add(Button("↓", () => scheduler.MoveAsync(item.Id, 1)));
            actions.Children.Add(Button(view.ParallelLabel, () => scheduler.UpdateAsync(item.Id, c => c.Parallel = !c.Parallel)));
            actions.Children.Add(Button(view.PauseLabel, () => scheduler.UpdateAsync(item.Id, c => c.Paused = !c.Paused)));
            actions.Children.Add(Button("Retry / rearm", async () =>
            {
                if (await Dialogs.Confirm(this, "Inspect before retrying", "Inspect the original conversation and resolve required approvals or errors in a supported Codex client. Close that client again. If delivery was uncertain, verify whether the previous message arrived.\n\nConfirm inspection and permit one new continuation?"))
                { await scheduler.RearmAsync(item.Id, true); await scheduler.TickAsync(); }
            }));
            actions.Children.Add(Button("Prompt…", async () => { if (await Dialogs.Prompt(this, "Continuation prompt", item.Prompt) is { } prompt) await scheduler.UpdateAsync(item.Id, c => c.Prompt = prompt); }));
            actions.Children.Add(Button("Remove", () => scheduler.RemoveAsync(item.Id)));
            cards.Children.Add(new Border { Background = Brush.Parse("#1B293C"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = new StackPanel { Spacing = 7, Children = {
                heading, Text(view.Id, "#A6BDD9"), Text(view.Workspace, "#A6BDD9"), timing, Text(view.Reason), actions } } });
        }
    }
}
