using System.Runtime.InteropServices;

namespace CodexAutoResume.Core;

/// <summary>Finds a native CLI without executing npm shims, shells, or Desktop.</summary>
public static class CodexExecutableResolver
{
    public static bool IsAutomatic(string? value) => Normalize(value).ToLowerInvariant() is
        "" or "codex" or "codex.exe" or "codex.cmd" or "codex.ps1";

    public static string Resolve(string? value = null) => Find(value) ?? throw new FileNotFoundException(
        "Could not find the native Codex CLI. Use Find Codex or Browse to select codex.exe. " +
        "If Codex is not installed, install the Windows Codex CLI and sign in first. npm wrappers are detected automatically.");

    public static string? Find(string? value = null, IEnumerable<string>? searchDirectories = null)
    {
        var selected = Normalize(value);
        if (!IsAutomatic(selected))
        {
            // An explicit choice must not silently switch to a different installation.
            if (!File.Exists(selected)) return null;
            if (Path.GetExtension(selected).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(selected);
            if (Path.GetExtension(selected).ToLowerInvariant() is not (".cmd" or ".ps1" or ".bat")) return null;
            return FindInDirectory(Path.GetDirectoryName(Path.GetFullPath(selected))!);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in searchDirectories ?? DefaultDirectories())
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            string directory;
            try { directory = Path.GetFullPath(Normalize(entry)); }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { continue; }
            if (!seen.Add(directory)) continue;
            // Desktop's WindowsApps launcher is not a native CLI discovery target.
            if (directory.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar })
                .Any(part => part.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase))) continue;
            if (FindInDirectory(directory) is { } executable) return executable;
        }
        return null;
    }

    private static string? FindInDirectory(string directory)
    {
        var direct = Path.Combine(directory, "codex.exe");
        if (File.Exists(direct)) return direct;

        var targets = new[] { (Package: "codex-win32-x64", Triple: "x86_64-pc-windows-msvc"),
            (Package: "codex-win32-arm64", Triple: "aarch64-pc-windows-msvc") };
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64) Array.Reverse(targets);
        var roots = new[] { Path.Combine(directory, "node_modules", "@openai", "codex"), directory };
        foreach (var target in targets)
        foreach (var root in roots)
        {
            var packages = new[] { Path.Combine(root, "node_modules", "@openai", target.Package),
                Path.Combine(directory, "node_modules", "@openai", target.Package), root };
            foreach (var package in packages)
            foreach (var binaryDirectory in new[] { "bin", "codex" })
            {
                var candidate = Path.Combine(package, "vendor", target.Triple, binaryDirectory, "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static IEnumerable<string> DefaultDirectories()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            yield return directory;
        yield return Environment.GetEnvironmentVariable("NPM_CONFIG_PREFIX") ?? "";
        foreach (var (folder, child) in new[] {
            (Environment.SpecialFolder.ApplicationData, "npm"),
            (Environment.SpecialFolder.LocalApplicationData, "pnpm"),
            (Environment.SpecialFolder.ProgramFiles, "nodejs") })
        {
            var parent = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(parent)) yield return Path.Combine(parent, child);
        }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local)) yield return Path.Combine(local, "Microsoft", "WinGet", "Links");
    }

    private static string Normalize(string? value) => Environment.ExpandEnvironmentVariables((value ?? "").Trim().Trim('"'));
}
