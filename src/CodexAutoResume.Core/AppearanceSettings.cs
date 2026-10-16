using System.Globalization;

namespace CodexAutoResume.Core;

public sealed record AppearanceSettings
{
    public string Accent { get; init; } = ThemePalette.SuckerPunchPinkPreview;
    public bool ReduceMotion { get; init; }
    public bool ReduceGlow { get; init; }
    public bool MinimizeToTray { get; init; } = true;
    public bool CloseToTray { get; init; } = true;
}

public static class ThemePalette
{
    // BRAND_COLOR_TODO: original SuckerPunch assets are not available in this repo.
    public const string SuckerPunchPinkPreview = "#FF4FA3";
    public const string Background = "#080D0D", Surface = "#0E1716", Elevated = "#121E1A",
        Border = "#29413A", Text = "#DFF3E8", Secondary = "#8DA69A",
        Success = "#98E6AE", Attention = "#FFBC70", Failure = "#FF8794";
    public static IReadOnlyDictionary<string, string> Presets { get; } = new Dictionary<string, string>
    {
        ["SuckerPunch Pink"] = SuckerPunchPinkPreview, ["Phosphor Green"] = "#C3FF68",
        ["Electric Cyan"] = "#55DDFF", ["Ultraviolet"] = "#B998FF", ["Amber"] = "#FFBC70"
    };
    public static bool TryNormalize(string? text, out string hex)
    {
        text = text?.Trim(); hex = "";
        if (text is not { Length: 7 } || text[0] != '#' || !int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return false;
        hex = text.ToUpperInvariant(); return true;
    }
    public static string Mix(string foreground, string background, double weight)
    {
        int Channel(int offset) => (int)Math.Round(Convert.ToInt32(foreground.Substring(offset, 2), 16) * weight + Convert.ToInt32(background.Substring(offset, 2), 16) * (1 - weight));
        return $"#{Channel(1):X2}{Channel(3):X2}{Channel(5):X2}";
    }
    private static double Luminance(string hex)
    {
        double Channel(int offset) { var value = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        return .2126 * Channel(1) + .7152 * Channel(3) + .0722 * Channel(5);
    }
    public static double Contrast(string a, string b) => (Math.Max(Luminance(a), Luminance(b)) + .05) / (Math.Min(Luminance(a), Luminance(b)) + .05);
    public static string AccentText(string accent)
    {
        for (double weight = 0; weight <= 1; weight += .05)
        {
            var adjusted = Mix(Text, accent, weight);
            if (Contrast(adjusted, Surface) >= 4.5) return adjusted;
        }
        return Text;
    }
}

public record DashboardStatus(string Heading, string NextAction, string Health)
{
    public static DashboardStatus From(AppState state, bool connected, string? failure = null)
    {
        if (failure != null) return new("Connection needs attention", failure, ThemePalette.Failure);
        if (!connected) return new("Watcher disconnected", "Close other Codex clients, confirm exclusive ownership, then Connect.", ThemePalette.Attention);
        if (!state.Enabled) return new("Connected · scheduling OFF", "Switch ON to schedule eligible, revalidated conversations.", ThemePalette.Secondary);
        if (state.Conversations.Any(c => !c.Revalidated)) return new("Waiting for revalidation", "Saved conversation states must be verified before scheduling.", ThemePalette.Attention);
        if (state.Conversations.Any(c => c.Attention)) return new("Manual action required", "Inspect the conversation reason, resolve it, then Retry / rearm.", ThemePalette.Attention);
        if (state.Conversations.Any(c => c.WaitingForUsage)) return new("Waiting for Codex usage", "Resume only after Codex verifies that usage is available. See each conversation’s reset estimate.", ThemePalette.Attention);
        return new("Watcher verified · scheduling ON", state.Conversations.Count == 0 ? "Add an existing conversation to begin." : "Eligible conversations follow priority and repository conflict checks.", ThemePalette.Success);
    }
}
