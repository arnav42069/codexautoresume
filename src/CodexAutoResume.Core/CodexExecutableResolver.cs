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

    public static string? Find(string? value = null, IEnumerable<string>? searchDirectories = null, string? platform = null)
    {
        platform ??= OperatingSystem.IsWindows() ? "win32" : OperatingSystem.IsMacOS() ? "darwin" : "linux";
        var selected = Normalize(value);
        if (!IsAutomatic(selected))
        {
            // An explicit choice must not silently switch to a different installation.
            if (!File.Exists(selected)) return null;
            if (IsNative(selected, platform))
                return Path.GetFullPath(selected);
            if (platform == "win32" && Path.GetExtension(selected).ToLowerInvariant() is not (".cmd" or ".ps1" or ".bat")) return null;
            return FindInDirectory(Path.GetDirectoryName(Path.GetFullPath(selected))!, platform);
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
            if (FindInDirectory(directory, platform) is { } executable) return executable;
        }
        return null;
    }

    private static string? FindInDirectory(string directory, string platform)
    {
        var direct = Path.Combine(directory, platform == "win32" ? "codex.exe" : "codex");
        if (IsNative(direct, platform)) return direct;

        var targets = platform switch
        {
            "win32" => new[] { (Package: "codex-win32-x64", Triple: "x86_64-pc-windows-msvc"), (Package: "codex-win32-arm64", Triple: "aarch64-pc-windows-msvc") },
            "darwin" => new[] { (Package: "codex-darwin-x64", Triple: "x86_64-apple-darwin"), (Package: "codex-darwin-arm64", Triple: "aarch64-apple-darwin") },
            _ => new[] { (Package: "codex-linux-x64", Triple: "x86_64-unknown-linux-musl"), (Package: "codex-linux-arm64", Triple: "aarch64-unknown-linux-musl") }
        };
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64) Array.Reverse(targets);
        var roots = new List<string> { Path.Combine(directory, "node_modules", "@openai", "codex"), directory };
        if (platform != "win32" && File.Exists(direct))
        {
            var resolved = File.ResolveLinkTarget(direct, true)?.FullName ?? direct;
            var bin = Path.GetDirectoryName(resolved)!;
            roots.Add(bin); if (Directory.GetParent(bin) is { } parent) roots.Add(parent.FullName);
        }
        foreach (var target in targets)
        foreach (var root in roots)
        {
            var packages = new[] { Path.Combine(root, "node_modules", "@openai", target.Package),
                Path.Combine(directory, "node_modules", "@openai", target.Package), root };
            foreach (var package in packages)
            foreach (var binaryDirectory in new[] { "bin", "codex" })
            {
                var candidate = Path.Combine(package, "vendor", target.Triple, binaryDirectory, platform == "win32" ? "codex.exe" : "codex");
                if (IsNative(candidate, platform)) return candidate;
            }
        }
        return null;
    }

    private static IEnumerable<string> DefaultDirectories()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            yield return directory;
        yield return Environment.GetEnvironmentVariable("NPM_CONFIG_PREFIX") ?? "";
        yield return Environment.GetEnvironmentVariable("npm_config_prefix") ?? "";
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
        if (!OperatingSystem.IsWindows())
        {
            yield return "/usr/local/bin"; yield return "/usr/bin"; yield return "/opt/homebrew/bin";
            yield return "/usr/local/lib"; yield return "/usr/lib";
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var root in new[] { Path.Combine(Environment.GetEnvironmentVariable("NVM_DIR") ?? Path.Combine(home, ".nvm"), "versions", "node"),
                Path.Combine(home, ".local", "share", "fnm", "node-versions"), Path.Combine(home, "Library", "Application Support", "fnm", "node-versions"),
                Path.Combine(home, ".asdf", "installs", "nodejs") })
            {
                string[] versions;
                try { versions = Directory.Exists(root) ? Directory.GetDirectories(root).OrderByDescending(v => v, StringComparer.Ordinal).Take(32).ToArray() : []; }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (var version in versions) { yield return Path.Combine(version, "bin"); yield return Path.Combine(version, "installation", "bin"); }
            }
        }
    }

    private static bool IsNative(string path, string platform)
    {
        if (!File.Exists(path)) return false;
        if (platform == "win32") return Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        try
        {
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0) return false;
            using var file = File.OpenRead(path); var header = new byte[4];
            if (file.Read(header) != 4) return false;
            var magic = Convert.ToHexString(header);
            return platform == "linux" ? magic == "7F454C46" : magic is "CFFAEDFE" or "FEEDFACF" or "CAFEBABE" or "BEBAFECA";
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string Normalize(string? value) => Environment.ExpandEnvironmentVariables((value ?? "").Trim().Trim('"'));
}
