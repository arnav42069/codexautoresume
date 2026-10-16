using Avalonia;
using CodexAutoResume.Core;

namespace CodexAutoResume.Desktop;

internal static class Program
{
    public static bool SelfTest;
    public static string? Screenshot;
    public static bool StartInTray;
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version")) { Console.WriteLine("0.2.0"); return 0; }
        if (args.Contains("--create-shortcut") || args.Contains("--ensure-shortcut"))
        {
            try { Console.WriteLine(DesktopIntegration.CreateShortcut(Environment.ProcessPath!, force: args.Contains("--create-shortcut"))); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
        SelfTest = args.Contains("--self-test"); StartInTray = args.Contains("--tray");
        var image = Array.IndexOf(args, "--screenshot");
        if (image >= 0 && image + 1 < args.Length) Screenshot = Path.GetFullPath(args[image + 1]);
        try { return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex) { Console.Error.WriteLine("Codex AutoResume could not start: " + ex.Message); return 1; }
    }
}
