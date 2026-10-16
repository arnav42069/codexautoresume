using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace CodexAutoResume.Core;

public static class DesktopIntegration
{
    private const string Owner = "X-CodexAutoResume=true";
    public static string DataDirectory => Environment.GetEnvironmentVariable("AUTORESUME_DATA_HOME") is { Length: > 0 } custom
        ? Path.GetFullPath(custom) : OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexAutoResume")
        : OperatingSystem.IsMacOS()
        ? Path.Combine(Home, "Library", "Application Support", "CodexAutoResume")
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_STATE_HOME") ?? Path.Combine(Home, ".local", "state"), "codexautoresume");
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string ConfigHome => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Home, ".config");

    public static string CreateShortcut(string executable, bool force = false)
    {
        executable = Path.GetFullPath(executable);
        Directory.CreateDirectory(DataDirectory);
        var marker = Path.Combine(DataDirectory, "desktop-shortcut.json");
        string? previous = null;
        if (File.Exists(marker))
        {
            previous = JsonSerializer.Deserialize<Shortcut>(File.ReadAllText(marker))?.Target;
            // A manually deleted shortcut stays deleted unless explicitly requested.
            if (!force && previous == ShortcutTarget(executable)) return "Desktop shortcut already configured";
        }
        var desktop = DesktopDirectory();
        Directory.CreateDirectory(desktop);
        var target = ShortcutTarget(executable);
        string destination;
        if (OperatingSystem.IsWindows())
        {
            destination = Path.Combine(desktop, "Codex AutoResume.lnk");
            const string script = """
                $ErrorActionPreference = 'Stop'
                $shell = New-Object -ComObject WScript.Shell
                $link = $shell.CreateShortcut($env:AUTORESUME_LINK)
                if ((Test-Path -LiteralPath $env:AUTORESUME_LINK) -and
                    $link.TargetPath -ne $env:AUTORESUME_TARGET -and
                    ($env:AUTORESUME_PREVIOUS -eq '' -or $link.TargetPath -ne $env:AUTORESUME_PREVIOUS)) {
                    throw 'An unrelated shortcut already uses this name; it was preserved.'
                }
                $link.TargetPath = $env:AUTORESUME_TARGET
                $link.WorkingDirectory = [IO.Path]::GetDirectoryName($env:AUTORESUME_TARGET)
                $link.Description = 'Codex AutoResume'
                $link.IconLocation = $env:AUTORESUME_TARGET + ',0'
                $link.Save()
                """;
            var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script }) info.ArgumentList.Add(arg);
            info.Environment["AUTORESUME_LINK"] = destination; info.Environment["AUTORESUME_TARGET"] = target;
            info.Environment["AUTORESUME_PREVIOUS"] = previous ?? "";
            using var process = Process.Start(info) ?? throw new IOException("Could not start the Windows shortcut service.");
            if (!process.WaitForExit(15000)) { process.Kill(); throw new IOException("Shortcut creation timed out."); }
            if (process.ExitCode != 0) throw new IOException("Could not create the desktop shortcut. Check desktop folder permissions.");
        }
        else if (OperatingSystem.IsMacOS())
        {
            destination = Path.Combine(desktop, "Codex AutoResume.app");
            var link = new DirectoryInfo(destination);
            if (link.LinkTarget is { } existing)
            {
                if (existing != previous && existing != target) throw new IOException("An unrelated desktop item already uses this name; it was preserved.");
                link.Delete();
            }
            else if (link.Exists || File.Exists(destination)) throw new IOException("An unrelated desktop item already uses this name; it was preserved.");
            Directory.CreateSymbolicLink(destination, target);
        }
        else
        {
            destination = Path.Combine(desktop, "Codex AutoResume.desktop");
            WriteDesktopEntry(destination, executable);
            var menu = Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Home, ".local", "share"), "applications", "codexautoresume.desktop");
            WriteDesktopEntry(menu, executable);
        }
        var temporary = marker + "." + Guid.NewGuid() + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(new Shortcut(target, destination))); File.Move(temporary, marker, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return destination;
    }

    public static void SetStartAtLogin(bool enabled, string executable)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled) key.SetValue("CodexAutoResume", "\"" + executable + "\" --tray");
            else key.DeleteValue("CodexAutoResume", false);
        }
        else if (OperatingSystem.IsMacOS())
        {
            var path = Path.Combine(Home, "Library", "LaunchAgents", "dev.arnav.codexautoresume.plist");
            if (!enabled) { if (File.Exists(path)) File.Delete(path); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
                new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
                    new XElement("key", "Label"), new XElement("string", "dev.arnav.codexautoresume"),
                    new XElement("key", "ProgramArguments"), new XElement("array", new XElement("string", executable), new XElement("string", "--tray")),
                    new XElement("key", "RunAtLoad"), new XElement("true"))));
            doc.Save(path);
        }
        else
        {
            var path = Path.Combine(ConfigHome, "autostart", "codexautoresume.desktop");
            if (enabled) WriteDesktopEntry(path, executable, "--tray");
            else if (File.Exists(path) && File.ReadAllText(path).Contains(Owner)) File.Delete(path);
        }
    }

    public static bool OtherCodexClientsAbsent(int? ownedProcess)
    {
        foreach (var process in Process.GetProcesses())
        using (process)
        {
            try { if (process.Id != ownedProcess && process.ProcessName.Equals("codex", StringComparison.OrdinalIgnoreCase)) return false; }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }
        return true;
    }

    public static string DesktopEntry(string executable, string arguments = "")
    {
        if (executable.Any(char.IsControl)) throw new ArgumentException("Invalid executable path.");
        var escaped = executable.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%");
        return $"[Desktop Entry]\nType=Application\nName=Codex AutoResume\nComment=Resume interrupted Codex work\nExec=\"{escaped}\" {arguments}\nIcon={Path.Combine(AppContext.BaseDirectory, "icon.png")}\nTerminal=false\nCategories=Development;Utility;\n{Owner}\n";
    }

    private static void WriteDesktopEntry(string path, string executable, string arguments = "")
    {
        if (File.Exists(path) && !File.ReadAllText(path).Contains(Owner)) throw new IOException("An unrelated desktop entry already uses this name; it was preserved.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, DesktopEntry(executable, arguments), new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }

    private static string ShortcutTarget(string executable)
    {
        var parent = Directory.GetParent(executable);
        return OperatingSystem.IsMacOS() && parent?.Name == "MacOS" && parent.Parent?.Name == "Contents"
            ? parent.Parent.Parent!.FullName : executable;
    }

    private static string DesktopDirectory()
    {
        if (Environment.GetEnvironmentVariable("AUTORESUME_DESKTOP_DIR") is { Length: > 0 } custom) return Path.GetFullPath(custom);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            var known = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return string.IsNullOrEmpty(known) ? Path.Combine(Home, "Desktop") : known;
        }
        var config = Path.Combine(ConfigHome, "user-dirs.dirs");
        if (File.Exists(config))
            foreach (var line in File.ReadLines(config))
                if (line.StartsWith("XDG_DESKTOP_DIR=\"", StringComparison.Ordinal) && line.EndsWith('"'))
                {
                    var value = line[17..^1].Replace("$HOME", Home);
                    if (Path.IsPathFullyQualified(value)) return value;
                }
        return Path.Combine(Home, "Desktop");
    }

    private sealed record Shortcut(string Target, string Destination);
}
