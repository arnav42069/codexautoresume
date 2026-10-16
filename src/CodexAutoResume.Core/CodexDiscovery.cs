using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CodexAutoResume.Core;

public record DetectedCodex(string Path, string Version);

/// <summary>Discovery only runs --version. It never creates an app-server or signs in.</summary>
public static partial class CodexDiscovery
{
    [GeneratedRegex(@"^codex-cli\s+\d+\.\d+\.\d+(?:[^\r\n]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    public static async Task<DetectedCodex> ValidateAsync(string selected, CancellationToken ct = default)
    {
        var native = CodexExecutableResolver.Resolve(selected);
        var output = await ExecuteAsync(native, ["--version"], ct);
        if (!VersionPattern().IsMatch(output))
            throw new InvalidDataException($"{native}: --version did not identify a Codex CLI. Choose an official Codex installation.");
        return new(native, output);
    }

    public static async Task<DetectedCodex> DiscoverAsync(string? savedPath = null, bool manualOverride = false,
        CancellationToken ct = default, IEnumerable<string>? searchDirectories = null,
        Func<CancellationToken, Task<string?>>? npmRoot = null)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        async Task<DetectedCodex?> Check(string candidate)
        {
            if (!seen.Add(candidate)) return null;
            try { return await ValidateAsync(candidate, ct); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
            { errors.Add(ex.Message); return null; }
        }
        if (!CodexExecutableResolver.IsAutomatic(savedPath))
        {
            if (await Check(savedPath!) is { } saved) return saved;
            if (manualOverride) throw new IOException("The selected Codex executable is invalid. " + string.Join(" ", errors));
        }
        var directories = (searchDirectories ?? CodexExecutableResolver.DefaultDirectories()).ToList();
        // PATH enumeration is the equivalent of where codex / where codex.exe;
        // npm .cmd/.ps1 launchers are resolved to vendor binaries, never executed.
        foreach (var directory in directories)
            if (await CheckDirectory(directory) is { } found) return found;
        var root = await (npmRoot ?? QueryNpmRootAsync)(ct);
        if (root != null)
        {
            directories.Add(Path.Combine(root, "@openai", "codex"));
            directories.Add(root);
            foreach (var directory in directories.TakeLast(2))
                if (await CheckDirectory(directory) is { } found) return found;
        }
        // Bounded fallback only inside known Codex packages, never a disk scan.
        foreach (var directory in directories.Where(d => !string.IsNullOrWhiteSpace(d)))
        foreach (var package in new[] { Path.Combine(directory.Trim('"'), "node_modules", "@openai", "codex"),
            Path.Combine(directory.Trim('"'), "@openai", "codex") })
        foreach (var candidate in PackageBinaries(package))
            if (await Check(candidate) is { } found) return found;
        throw new FileNotFoundException("Codex CLI was not found or could not be validated. Install @openai/codex, sign in separately, then Re-detect. " +
            string.Join(" ", errors.TakeLast(3)));

        async Task<DetectedCodex?> CheckDirectory(string directory)
        {
            try
            {
                if (CodexExecutableResolver.Find(null, [directory]) is { } candidate) return await Check(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { errors.Add(ex.Message); }
            return null;
        }
    }

    private static IEnumerable<string> PackageBinaries(string root)
    {
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((root, 0));
        int visited = 0;
        while (pending.Count > 0 && visited++ < 512)
        {
            var (path, depth) = pending.Pop();
            string[] files, dirs;
            try
            {
                if (!Directory.Exists(path) || new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                files = Directory.GetFiles(path, OperatingSystem.IsWindows() ? "codex.exe" : "codex");
                dirs = depth < 8 ? Directory.GetDirectories(path) : [];
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var file in files.Take(16)) yield return file;
            foreach (var dir in dirs.Take(64)) pending.Push((dir, depth + 1));
        }
    }

    public static async Task<string?> QueryNpmRootAsync(CancellationToken ct)
    {
        // npm.cmd cannot be launched without a shell. Use node + npm-cli.js with
        // ArgumentList so custom prefixes and paths containing spaces stay literal.
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim('"')).ToArray();
        foreach (var directory in paths)
        {
            var node = Path.Combine(directory, OperatingSystem.IsWindows() ? "node.exe" : "node");
            if (!File.Exists(node)) continue;
            var roots = paths.Concat([directory, Path.Combine(directory, "..", "lib")]);
            foreach (var prefix in roots)
            foreach (var cli in new[] { Path.Combine(prefix, "node_modules", "npm", "bin", "npm-cli.js"), Path.Combine(prefix, "npm", "bin", "npm-cli.js") })
            {
                if (!File.Exists(cli)) continue;
                try
                {
                    var result = await ExecuteAsync(node, [Path.GetFullPath(cli), "root", "-g"], ct);
                    if (Path.IsPathFullyQualified(result) && Directory.Exists(result)) return result;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
            }
        }
        return null;
    }

    private static async Task<string> ExecuteAsync(string path, IEnumerable<string> arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        using var p = Process.Start(info) ?? throw new IOException("Cannot start " + path);
        p.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var output = p.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = p.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await p.WaitForExitAsync(timeout.Token);
            var text = (await output).Trim(); await errors;
            if (p.ExitCode != 0) throw new IOException($"{path}: command exited with code {p.ExitCode}. Reinstall Codex or choose another installation.");
            return text;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new IOException($"{path}: executable validation timed out. Re-detect or select another installation."); }
        finally
        {
            if (!p.HasExited) try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(output, errors); } catch (OperationCanceledException) { }
        }
    }
}
