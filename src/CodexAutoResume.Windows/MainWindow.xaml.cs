using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using CodexAutoResume.Core;
using Forms = System.Windows.Forms;

namespace CodexAutoResume.Windows;

public partial class MainWindow : Window
{
    private readonly Scheduler scheduler;
    private readonly AppServerClient client;
    private readonly AutoConnection autoConnection;
    private readonly ObservableCollection<ConversationView> rows = [];
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer clock;
    private AppState state;
    private bool exiting, busy, appearanceReady, settingPath, manualPath;
    private string? connectionFailure;
    private readonly List<string> localEvents = [];
    private readonly DispatcherTimer appearanceSave = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private AppearanceSettings appearance = new();
    public static readonly DependencyProperty MotionEnabledProperty = DependencyProperty.Register(nameof(MotionEnabled), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public bool MotionEnabled { get => (bool)GetValue(MotionEnabledProperty); private set => SetValue(MotionEnabledProperty, value); }

    public MainWindow(Scheduler scheduler, AppServerClient client, bool autoDetect = true)
    {
        this.scheduler = scheduler; this.client = client; autoConnection = new(scheduler); state = scheduler.Snapshot;
        appearance = state.Appearance;
        PhosphorTheme.Apply(appearance);
        InitializeComponent();
        SourceInitialized += (_, _) => PhosphorTheme.DarkTitleBar(this);
        SystemParameters.StaticPropertyChanged += AccessibilityChanged;
        LoadAppearance();
        appearanceSave.Tick += async (_, _) =>
        {
            appearanceSave.Stop();
            try { await scheduler.SaveAppearanceAsync(appearance); }
            catch (Exception ex) { AccentValidation.Text = "Could not save appearance: " + ex.Message; }
        };
        Cards.ItemsSource = rows; SetExecutable(state.CodexPath);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Dispatcher.Invoke(Restore));
        menu.Items.Add("Pause / resume scheduling", null, (_, _) => Dispatcher.Invoke(async () => await Run(async () => { await scheduler.SetEnabledAsync(!state.Enabled); await scheduler.TickAsync(); })));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(async () => await ExitAsync()));
        tray = new Forms.NotifyIcon { Text = "Codex AutoResume · OFF", Icon = System.Drawing.SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
        scheduler.Changed += next => Dispatcher.BeginInvoke(new Action(() => Render(next)));
        scheduler.Notice += text => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (exiting) return;
            tray.BalloonTipTitle = "Codex AutoResume"; tray.BalloonTipText = text.Length > 200 ? text[..200] : text;
            tray.ShowBalloonTip(4000);
        }));
        clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += async (_, _) =>
        {
            foreach (var row in rows) row.RefreshClock();
            if (!scheduler.IsConnected && !exiting) await Run(() => ConnectAsync(force: false));
        };
        clock.Start(); Render(state);
        if (autoDetect)
            Loaded += async (_, _) => await Run(() => ConnectAsync(force: true));
    }

    private void Render(AppState next)
    {
        if (exiting) return;
        state = next;
        if (!scheduler.IsConnected && connectionFailure == null) Connection.Text = autoConnection.Status;
        var status = DashboardStatus.From(state, scheduler.IsConnected, connectionFailure, scheduler.HasExecutionOwnership);
        WatcherHeading.Text = status.Heading;
        WatcherHeading.Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush((Color)ColorConverter.ConvertFromString(status.Health));
        NextAction.Text = status.NextAction;
        ConnectionBadge.Text = !scheduler.IsConnected ? "DISCONNECTED" : scheduler.HasExecutionOwnership ? "OWNED EXECUTION · CONNECTED" : "CONNECTED · READ-ONLY";
        AcquireButton.IsEnabled = scheduler.IsConnected && !scheduler.HasExecutionOwnership;
        RefreshChatsButton.IsEnabled = scheduler.IsConnected && !scheduler.HasExecutionOwnership;
        Exclusive.IsEnabled = !scheduler.HasExecutionOwnership;
        ConnectButton.IsEnabled = !scheduler.IsConnected && client.ProcessId == null;
        ConnectionErrorPanel.Visibility = connectionFailure == null ? Visibility.Collapsed : Visibility.Visible;
        ConnectionError.Text = connectionFailure ?? "";
        AddButton.IsEnabled = scheduler.IsConnected && state.Conversations.Count < 5;
        Toggle.Content = state.Enabled ? "● ON" : "○ OFF";
        tray.Text = state.Enabled ? "Codex AutoResume · ON" : "Codex AutoResume · OFF";
        Startup.IsChecked = state.StartWithWindows;
        EmptyPanel.Visibility = state.Conversations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var queued = state.Conversations.Where(c => c.Status == ConversationStatus.Queued).Select(c => c.Id).ToList();
        foreach (var old in rows.Where(r => state.Conversations.All(c => c.Id != r.Id)).ToList()) rows.Remove(old);
        for (var i = 0; i < state.Conversations.Count; i++)
        {
            var c = state.Conversations[i]; var row = rows.FirstOrDefault(r => r.Id == c.Id);
            if (row == null) { row = new(c); rows.Insert(i, row); }
            else if (rows.IndexOf(row) != i) rows.Move(rows.IndexOf(row), i);
            row.Update(c, i + 1, queued.IndexOf(c.Id) + 1);
        }
        int Count(ConversationStatus status) => state.Conversations.Count(c => !c.ObservationOnly && c.Status == status || status == ConversationStatus.Running && c.ObservationOnly && c.ObservedActive);
        Summary.Text = $"{Count(ConversationStatus.Running)} running · {Count(ConversationStatus.Queued)} queued · {Count(ConversationStatus.RateLimited)} rate limited · {Count(ConversationStatus.Completed)} completed";
        var saved = state.Conversations.Any(c => !c.ObservationOnly && !c.Revalidated);
        if (saved) Summary.Text = "Last known: " + Summary.Text;
        string Total(ConversationStatus status) => Count(status) + (saved ? " *" : "");
        RunningCount.Text = Total(ConversationStatus.Running); QueuedCount.Text = Total(ConversationStatus.Queued);
        LimitedCount.Text = Total(ConversationStatus.RateLimited); CompletedCount.Text = Total(ConversationStatus.Completed);
        CompletedCount.SetResourceReference(TextBlock.ForegroundProperty, saved ? "Secondary" : "Success");
        var limited = state.Conversations.Where(c => c.WaitingForUsage).ToList();
        Usage.Text = !scheduler.HasExecutionOwnership ? (state.DiscoveryError ?? state.DiscoveryOrder) : limited.Count == 0 ? "Usage is checked before scheduling; reset permission is verified with Codex." :
            limited.Any(c => c.ResetAt == null) ? "Usage limited · some reset estimates unavailable · backoff enabled" :
            $"Usage limited · estimated reset {limited.Max(c => c.ResetAt)!.Value.ToLocalTime():g}";
        var selected = (Activity.SelectedItem as EventRow)?.Message;
        Activity.ItemsSource = localEvents.Concat(state.Log).Select(EventRow.From).ToArray();
        if (selected != null) Activity.SelectedItem = Activity.Items.Cast<EventRow>().FirstOrDefault(row => row.Message == selected);
    }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        try { await action(); }
        catch (Exception ex) { RecordError(ex.Message); }
        finally { busy = false; }
    }
    private async void ConnectClick(object sender, RoutedEventArgs e) => await Run(() => ConnectAsync(force: true));
    private async Task ConnectAsync(bool force = true)
    {
        if (scheduler.IsConnected || !force && autoConnection.NextRetryAt > DateTimeOffset.UtcNow) return;
        SetConnectionControls(false);
        try
        {
            Connection.Text = "Validating Codex and connecting read-only…";
            if (!await autoConnection.TryConnectAsync(CodexPath.Text, manualPath, force)) return;
            if (autoConnection.Error is { } error) RecordError(error);
            else if (autoConnection.Detected is { } found)
            {
                SetExecutable(found.Path); DetectedVersion.Text = found.Version;
                Detection.Text = "Native executable validated. Reconnects automatically; Desktop may remain open.";
                await scheduler.SaveExecutableAsync(found.Path);
                connectionFailure = null;
            }
            Connection.Text = autoConnection.Status;
        }
        finally { SetConnectionControls(!scheduler.IsConnected); Render(await scheduler.GetSnapshotAsync()); }
    }

    private async Task FindCodexAsync(bool showMissing) => await Run(async () =>
    {
        if (client.ProcessId != null) return;
        SetConnectionControls(false);
        var previous = CodexPath.Text;
        Detection.Text = "Checking saved path, PATH and npm installations…";
        try
        {
            var found = await CodexDiscovery.DiscoverAsync(showMissing ? null : previous);
            if (CodexPath.Text != previous) return;
            SetExecutable(found.Path); DetectedVersion.Text = found.Version;
            await scheduler.SaveExecutableAsync(found.Path);
            Detection.Text = "Detected and version-validated. Connect read-only; Desktop may stay open.";
        }
        catch (Exception ex) { DetectedVersion.Text = ""; Detection.Text = ex.Message; }
        finally { SetConnectionControls(client.ProcessId == null); }
    });

    private void SetConnectionControls(bool enabled)
    {
        ConnectButton.IsEnabled = FindButton.IsEnabled = BrowseButton.IsEnabled = CodexPath.IsEnabled = enabled;
        Exclusive.IsEnabled = !scheduler.HasExecutionOwnership;
    }
    private void SetExecutable(string path)
    {
        settingPath = true; CodexPath.Text = path; settingPath = false; manualPath = false;
    }
    private void CodexPathChanged(object sender, TextChangedEventArgs e)
    {
        if (settingPath || Detection == null) return;
        manualPath = true; DetectedVersion.Text = ""; Detection.Text = "Manual override · validation required before connecting.";
    }
    private async void FindCodexClick(object sender, RoutedEventArgs e) => await FindCodexAsync(showMissing: true);
    private async void BrowseCodexClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (client.ProcessId != null) return;
        var picker = new OpenFileDialog { Title = "Choose the native Codex CLI", Filter = "Codex executable (codex.exe)|codex.exe|Windows executables (*.exe)|*.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        CodexPath.Text = picker.FileName; manualPath = true;
        try
        {
            var found = await CodexDiscovery.DiscoverAsync(picker.FileName, manualOverride: true);
            SetExecutable(found.Path); manualPath = true; DetectedVersion.Text = found.Version;
            await scheduler.SaveExecutableAsync(found.Path);
            Detection.Text = "Manual override validated and saved. Connect to browse with Desktop open.";
        }
        catch (Exception ex) { Detection.Text = ex.Message; }
    });
    private void RecordError(string message)
    {
        connectionFailure = message;
        localEvents.Insert(0, $"{DateTimeOffset.UtcNow:u}  Error: {message}");
        if (localEvents.Count > 50) localEvents.RemoveAt(50);
        ConnectionSettings.IsExpanded = true;
        Render(state);
    }
    private async void AcquireClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        await scheduler.AcquireExecutionOwnershipAsync(Exclusive.IsChecked == true);
        connectionFailure = null; Connection.Text = "Execution ownership acquired. Review the queue, then switch ON.";
        Render(await scheduler.GetSnapshotAsync());
    });
    private async void RefreshChatsClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        await scheduler.RefreshDiscoveryAsync(); connectionFailure = null;
        Render(await scheduler.GetSnapshotAsync());
    });
    private async void ToggleClick(object sender, RoutedEventArgs e) => await Run(async () => { await scheduler.SetEnabledAsync(!state.Enabled); await scheduler.TickAsync(); });
    private async void AddClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var threads = await scheduler.DiscoverAsync();
        var id = Dialogs.PickConversation(this, threads);
        if (id == null) return;
        var thread = await scheduler.InspectAsync(id);
        if (MessageBox.Show(this, $"Add this exact conversation?\n\n{thread.Name}\nUUID: {thread.Id}\nWorkspace: {thread.Workspace}\nLatest turn: {thread.LastTurn?.Status ?? "none"}\n\nConfirm this is the intended session. Completed work will remain disarmed.",
            "Verify conversation identity", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await scheduler.AddAsync(id);
    });
    private static string Id(object sender) => (string)((Button)sender).Tag;
    private async void UpClick(object sender, RoutedEventArgs e) => await Run(() => scheduler.MoveAsync(Id(sender), -1));
    private async void DownClick(object sender, RoutedEventArgs e) => await Run(() => scheduler.MoveAsync(Id(sender), 1));
    private async void ParallelClick(object sender, RoutedEventArgs e) => await Run(() => scheduler.UpdateAsync(Id(sender), c => c.Parallel = !c.Parallel));
    private async void PauseClick(object sender, RoutedEventArgs e) => await Run(() => scheduler.UpdateAsync(Id(sender), c => c.Paused = !c.Paused));
    private async void RemoveClick(object sender, RoutedEventArgs e) => await Run(() => scheduler.RemoveAsync(Id(sender)));
    private async void RetryClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var c = state.Conversations.Single(c => c.Id == Id(sender));
        if (MessageBox.Show(this, $"Explicitly rearm {c.Name}?\n\nInspect the conversation first. Resolve required approvals or errors in a supported Codex client, close that client, then retry here. If delivery was uncertain, verify whether the previous continuation arrived.\n\nThis action acknowledges your inspection and permits one new continuation, even if earlier work completed.",
            "Reconcile and rearm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        { await scheduler.RearmAsync(c.Id, acknowledgeUncertain: true); await scheduler.TickAsync(); }
    });
    private async void PromptClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var c = state.Conversations.Single(c => c.Id == Id(sender));
        var prompt = Dialogs.EditPrompt(this, c.Prompt);
        if (prompt != null) await scheduler.UpdateAsync(c.Id, row => row.Prompt = prompt);
    });

    private async void StartupClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var enabled = Startup.IsChecked == true;
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new IOException("Cannot locate this executable.");
            key.SetValue("CodexAutoResume", $"\"{executable}\" --tray");
        }
        else key.DeleteValue("CodexAutoResume", false);
        await scheduler.SaveSettingsAsync(enabled);
    });
    private void HideClick(object sender, RoutedEventArgs e) => Hide();
    private async void ShortcutClick(object sender, RoutedEventArgs e) => await Run(() =>
    {
        Connection.Text = DesktopIntegration.CreateShortcut(Environment.ProcessPath!, force: true);
        return Task.CompletedTask;
    });
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return; e.Cancel = true;
        if (appearance.CloseToTray) Hide(); else await ExitAsync();
    }
    private void OnWindowStateChanged(object? sender, EventArgs e) { if (WindowState == WindowState.Minimized && appearance.MinimizeToTray) Hide(); }
    private async void ExitClick(object sender, RoutedEventArgs e) => await ExitAsync();
    private void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    private async Task ExitAsync()
    {
        if (busy) return;
        // Serialize OFF before checking live state, so a simultaneous scheduling tick
        // cannot dispatch a new turn between the check and shutting down the server.
        await Run(async () =>
        {
            await scheduler.SetEnabledAsync(false);
            var current = await scheduler.GetSnapshotAsync();
            if (current.Conversations.Any(c => c.ExecutionActive) || current.Attempts.Any(a => a.Phase is AttemptPhase.Prepared or AttemptPhase.Accepted))
            { MessageBox.Show(this, "Active or uncertain attempts remain. Scheduling is OFF; leave the utility in the tray until they finish or are reconciled."); return; }
            await FlushAppearanceAsync(); exiting = true; clock.Stop(); SystemParameters.StaticPropertyChanged -= AccessibilityChanged; await scheduler.DisposeAsync(); tray.Visible = false; tray.Dispose(); Application.Current.Shutdown();
        });
    }

    private void LoadAppearance()
    {
        appearanceReady = false;
        AccentPreset.ItemsSource = ThemePalette.Presets.Keys;
        AccentPreset.SelectedItem = ThemePalette.Presets.FirstOrDefault(p => p.Value == appearance.Accent).Key;
        AccentHex.Text = appearance.Accent;
        ReduceMotion.IsChecked = appearance.ReduceMotion; ReduceGlow.IsChecked = appearance.ReduceGlow;
        MinimizeTray.IsChecked = appearance.MinimizeToTray; CloseTray.IsChecked = appearance.CloseToTray;
        appearanceReady = true; ApplyAppearance();
    }
    private void ApplyAppearance()
    {
        PhosphorTheme.Apply(appearance); MotionEnabled = PhosphorTheme.MotionEnabled(appearance);
        TerminalMark.Effect = PhosphorTheme.MarkGlow(appearance);
    }
    private void AccessibilityChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (!exiting) { ApplyAppearance(); Render(state); }
    }));
    private void AppearanceChanged(object sender, RoutedEventArgs e)
    {
        if (!appearanceReady) return;
        appearance = appearance with { ReduceMotion = ReduceMotion.IsChecked == true, ReduceGlow = ReduceGlow.IsChecked == true,
            MinimizeToTray = MinimizeTray.IsChecked == true, CloseToTray = CloseTray.IsChecked == true };
        PreviewAndSave();
    }
    private void PreviewAndSave()
    {
        ApplyAppearance(); appearanceSave.Stop(); appearanceSave.Start();
    }
    private void AccentChanged(object sender, TextChangedEventArgs e)
    {
        if (!appearanceReady) return;
        if (!ThemePalette.TryNormalize(AccentHex.Text, out var accent))
        { AccentValidation.Text = "Use six HEX digits, for example #FF4FA3. The last valid color remains active."; return; }
        AccentValidation.Text = ""; appearance = appearance with { Accent = accent }; PreviewAndSave();
    }
    private void PresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (appearanceReady && AccentPreset.SelectedItem is string name) AccentHex.Text = ThemePalette.Presets[name];
    }
    private void PickAccentClick(object sender, RoutedEventArgs e)
    {
        using var picker = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(appearance.Accent) };
        if (picker.ShowDialog(new WindowHandle(this)) == Forms.DialogResult.OK)
        { AccentPreset.SelectedIndex = -1; AccentHex.Text = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}"; }
    }
    private sealed class WindowHandle(Window window) : Forms.IWin32Window
    { public IntPtr Handle => new System.Windows.Interop.WindowInteropHelper(window).Handle; }
    private void ResetAppearanceClick(object sender, RoutedEventArgs e)
    {
        appearance = new AppearanceSettings(); LoadAppearance(); PreviewAndSave();
    }
    private async Task FlushAppearanceAsync()
    {
        appearanceSave.Stop(); await scheduler.SaveAppearanceAsync(appearance);
    }
    private void EventSelected(object sender, SelectionChangedEventArgs e)
    { EventDetails.Text = (Activity.SelectedItem as EventRow)?.Message ?? ""; }

    internal async Task SmokeCloseAsync()
    {
        await FlushAppearanceAsync(); exiting = true; clock.Stop(); SystemParameters.StaticPropertyChanged -= AccessibilityChanged; await scheduler.DisposeAsync(); tray.Visible = false; tray.Dispose();
        Application.Current.Shutdown(0);
    }
}
