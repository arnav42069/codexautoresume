using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using CodexAutoResume.Core;
using Forms = System.Windows.Forms;

namespace CodexAutoResume.Windows;

public partial class MainWindow : Window
{
    private readonly Scheduler scheduler;
    private readonly AppServerClient client;
    private readonly ObservableCollection<ConversationView> rows = [];
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer clock;
    private AppState state;
    private bool exiting, busy;

    public MainWindow(Scheduler scheduler, AppServerClient client)
    {
        this.scheduler = scheduler; this.client = client; state = scheduler.Snapshot;
        InitializeComponent();
        Cards.ItemsSource = rows; CodexPath.Text = state.CodexPath;
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
        clock.Tick += (_, _) => { foreach (var row in rows) row.RefreshClock(); };
        clock.Start(); Render(state);
    }

    private void Render(AppState next)
    {
        if (exiting) return;
        state = next;
        if (!scheduler.IsConnected) Connection.Text = "Disconnected · connect and revalidate before scheduling";
        Toggle.Content = state.Enabled ? "● ON" : "○ OFF";
        tray.Text = state.Enabled ? "Codex AutoResume · ON" : "Codex AutoResume · OFF";
        Startup.IsChecked = state.StartWithWindows;
        Empty.Visibility = state.Conversations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var queued = state.Conversations.Where(c => c.Status == ConversationStatus.Queued).Select(c => c.Id).ToList();
        foreach (var old in rows.Where(r => state.Conversations.All(c => c.Id != r.Id)).ToList()) rows.Remove(old);
        for (var i = 0; i < state.Conversations.Count; i++)
        {
            var c = state.Conversations[i]; var row = rows.FirstOrDefault(r => r.Id == c.Id);
            if (row == null) { row = new(c); rows.Insert(i, row); }
            else if (rows.IndexOf(row) != i) rows.Move(rows.IndexOf(row), i);
            row.Update(c, i + 1, queued.IndexOf(c.Id) + 1);
        }
        int Count(ConversationStatus status) => state.Conversations.Count(c => c.Status == status);
        Summary.Text = $"{Count(ConversationStatus.Running)} running · {Count(ConversationStatus.Queued)} queued · {Count(ConversationStatus.RateLimited)} rate limited · {Count(ConversationStatus.Completed)} completed";
        if (state.Conversations.Any(c => !c.Revalidated)) Summary.Text = "Last known: " + Summary.Text;
        var limited = state.Conversations.Where(c => c.WaitingForUsage).ToList();
        Usage.Text = limited.Count == 0 ? "Usage is checked before scheduling; reset permission is verified with Codex." :
            limited.Any(c => c.ResetAt == null) ? "Usage limited · some reset estimates unavailable · backoff enabled" :
            $"Usage limited · estimated reset {limited.Max(c => c.ResetAt)!.Value.ToLocalTime():g}";
        Activity.Text = string.Join(Environment.NewLine, state.Log);
    }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        try { await action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Codex AutoResume", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { busy = false; }
    }
    private async void ConnectClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        ConnectButton.IsEnabled = false;
        Connection.Text = "Connecting and revalidating…";
        try
        {
            await scheduler.ConnectAsync(CodexPath.Text.Trim(), Exclusive.IsChecked == true);
            Connection.Text = $"Connected · {client.Version} · owned app-server";
            CodexPath.IsEnabled = Exclusive.IsEnabled = false;
        }
        catch { Connection.Text = "Connection failed. Resolve the error; restart AutoResume if the server was launched."; ConnectButton.IsEnabled = client.ProcessId == null; throw; }
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
    private void OnClosing(object? sender, CancelEventArgs e) { if (!exiting) { e.Cancel = true; Hide(); } }
    private void OnWindowStateChanged(object? sender, EventArgs e) { if (WindowState == WindowState.Minimized) Hide(); }
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
            exiting = true; clock.Stop(); await scheduler.DisposeAsync(); tray.Visible = false; tray.Dispose(); Application.Current.Shutdown();
        });
    }

    internal async Task SmokeCloseAsync()
    {
        exiting = true; clock.Stop(); await scheduler.DisposeAsync(); tray.Visible = false; tray.Dispose();
        Application.Current.Shutdown(0);
    }
}
