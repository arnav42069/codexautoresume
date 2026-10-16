using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CodexAutoResume.Core;

namespace CodexAutoResume.Windows;

internal static class PhosphorTheme
{
    public static void Apply(AppearanceSettings settings)
    {
        var resources = Application.Current.Resources;
        var accent = ThemePalette.TryNormalize(settings.Accent, out var valid) ? valid : ThemePalette.SuckerPunchPinkPreview;
        var tokens = new Dictionary<string, string> {
            ["Background"] = ThemePalette.Background, ["Surface"] = ThemePalette.Surface,
            ["Elevated"] = ThemePalette.Elevated, ["Border"] = ThemePalette.Border,
            ["Text"] = ThemePalette.Text, ["Secondary"] = ThemePalette.Secondary,
            ["Success"] = ThemePalette.Success, ["Attention"] = ThemePalette.Attention, ["Failure"] = ThemePalette.Failure,
            ["Accent"] = accent, ["AccentText"] = ThemePalette.AccentText(accent),
            ["AccentHover"] = ThemePalette.Mix(accent, ThemePalette.Elevated, .12),
            ["AccentFocus"] = ThemePalette.Mix(accent, ThemePalette.Border, .6)
        };
        foreach (var (name, color) in tokens) resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        if (SystemParameters.HighContrast)
        {
            foreach (var name in new[] { "Background", "Surface", "Elevated" }) resources[name] = SystemColors.WindowBrush;
            foreach (var name in new[] { "Text", "Secondary", "Success", "Attention", "Failure", "AccentText", "Border", "AccentFocus" }) resources[name] = SystemColors.WindowTextBrush;
            resources["AccentHover"] = SystemColors.HighlightBrush;
        }
        resources["TransitionDuration"] = new Duration(MotionEnabled(settings) ? TimeSpan.FromMilliseconds(150) : TimeSpan.Zero);
    }
    public static bool MotionEnabled(AppearanceSettings settings) => !settings.ReduceMotion && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    public static Effect? MarkGlow(AppearanceSettings settings) => settings.ReduceGlow || !MotionEnabled(settings) ? null :
        new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString(settings.Accent), BlurRadius = 7, ShadowDepth = 0, Opacity = .14 };
    public static void DarkTitleBar(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        int value = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref value, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}

internal record EventRow(string Severity, string Message, string Color)
{
    public static EventRow From(string message)
    {
        var lower = message.ToLowerInvariant();
        if (new[] { "failed", "error", "lost", "stopped", "disconnect" }.Any(lower.Contains)) return new("ERROR", message, ThemePalette.Failure);
        if (new[] { "attention", "limited", "waiting", "uncertain", "denied" }.Any(lower.Contains)) return new("WARN", message, ThemePalette.Attention);
        return new("INFO", message, ThemePalette.Secondary);
    }
}
