using System.Windows;
using System.Windows.Controls;
using CodexAutoResume.Core;

namespace CodexAutoResume.Windows;

internal static class Dialogs
{
    public static string? PickConversation(Window owner, IReadOnlyList<ThreadSnapshot> threads)
    {
        var window = NewWindow(owner, "Select an existing conversation", 680, 490);
        var panel = new DockPanel { Margin = new Thickness(16) };
        var help = new TextBlock { Text = "Select a discovered session or enter its exact UUID. The next step verifies its identity and workspace.", Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(help, Dock.Top); panel.Children.Add(help);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom);
        bottom.Children.Add(new TextBlock { Text = "Session UUID", Margin = new Thickness(0, 10, 0, 0) });
        var uuid = new TextBox(); bottom.Children.Add(uuid);
        var select = new Button { Content = "Verify selection", HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        bottom.Children.Add(select); panel.Children.Add(bottom);
        var list = new ListBox { Background = System.Windows.Media.Brushes.WhiteSmoke };
        foreach (var thread in threads)
            list.Items.Add(new ListBoxItem { Content = $"{thread.Name}\n{thread.Id}\n{thread.Workspace}", Tag = thread.Id, Padding = new Thickness(6) });
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem item) uuid.Text = (string)item.Tag; };
        panel.Children.Add(list);
        string? value = null;
        select.Click += (_, _) =>
        {
            if (!Guid.TryParse(uuid.Text.Trim(), out var id)) { MessageBox.Show(window, "Enter a valid session UUID."); return; }
            value = id.ToString(); window.DialogResult = true;
        };
        window.Content = panel;
        return window.ShowDialog() == true ? value : null;
    }

    public static string? EditPrompt(Window owner, string current)
    {
        var window = NewWindow(owner, "Continuation prompt", 600, 330);
        var panel = new DockPanel { Margin = new Thickness(16) };
        var save = new Button { Content = "Save prompt", HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        DockPanel.SetDock(save, Dock.Bottom); panel.Children.Add(save);
        var input = new TextBox { Text = current, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(input);
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) window.DialogResult = true; };
        window.Content = panel;
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }
    private static Window NewWindow(Window owner, string title, double width, double height) => new()
    {
        Owner = owner, Title = title, Width = width, Height = height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ShowInTaskbar = false, ResizeMode = ResizeMode.CanResize
    };
}
