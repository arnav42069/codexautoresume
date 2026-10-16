using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexAutoResume.Core;

namespace CodexAutoResume.Desktop;

internal static class Dialogs
{
    private static Window Create(Window owner, string title, Control content, double height = 300) => new()
    {
        Title = title, Width = 660, Height = height, MinWidth = 420, MinHeight = 220,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content,
        ShowInTaskbar = false
    };
    public static async Task<bool> Confirm(Window owner, string title, string text)
    {
        var yes = new Button { Content = "Confirm" }; var no = new Button { Content = "Cancel" };
        var content = new StackPanel { Margin = new Thickness(22), Spacing = 18, Children = {
            new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { yes, no } } } };
        var window = Create(owner, title, new ScrollViewer { Content = content }, 390);
        yes.Click += (_, _) => window.Close(true); no.Click += (_, _) => window.Close(false);
        return await window.ShowDialog<bool>(owner);
    }
    public static async Task<string?> Prompt(Window owner, string title, string current)
    {
        var input = new TextBox { Text = current, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 160 };
        var save = new Button { Content = "Save" };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 14, Children = { input, save } };
        var window = Create(owner, title, panel, 330);
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) window.Close(input.Text.Trim()); };
        return await window.ShowDialog<string?>(owner);
    }
    public static async Task<string?> Pick(Window owner, IReadOnlyList<ThreadSnapshot> threads)
    {
        var list = new ListBox { Height = 260, ItemsSource = threads.Select(t => new ListBoxItem {
            Content = new TextBlock { Text = $"{t.Name}\n{t.Id}\n{t.Workspace}", TextWrapping = TextWrapping.Wrap }, Tag = t.Id }).ToArray() };
        var input = new TextBox { Watermark = "Exact existing conversation UUID" };
        var error = new TextBlock { Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap };
        var select = new Button { Content = "Verify selection" };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children = {
            new TextBlock { Text = "Choose an existing session or enter its original UUID. No replacement thread will be created.", TextWrapping = TextWrapping.Wrap }, list, input, error, select } };
        var window = Create(owner, "Select a conversation", new ScrollViewer { Content = panel }, 540);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem item) input.Text = (string)item.Tag!; };
        select.Click += (_, _) =>
        {
            if (!Guid.TryParse(input.Text?.Trim(), out var id)) { error.Text = "Enter a valid conversation UUID."; return; }
            window.Close(id.ToString());
        };
        return await window.ShowDialog<string?>(owner);
    }
}
