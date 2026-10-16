using System.Diagnostics;

namespace CodexAutoResume.Core;

public static class WorkspaceIdentity
{
    public static async Task<string> GetAsync(string cwd, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(cwd) || !Directory.Exists(cwd))
            throw new IOException("The conversation workspace is missing or is not an absolute local path.");
        var full = PhysicalSystemPath(Path.TrimEndingDirectorySeparator(Path.GetFullPath(cwd)));
        RejectReparsePoints(full);
        // Fail closed if Git cannot establish the common directory. Shared worktrees are
        // intentionally serialized too; no repository files or worktrees are created.
        var info = new ProcessStartInfo("git") { WorkingDirectory = full, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "rev-parse", "--path-format=absolute", "--git-common-dir" }) info.ArgumentList.Add(arg);
        using var p = Process.Start(info) ?? throw new IOException("Cannot run Git to check workspace conflicts.");
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch { try { p.Kill(); } catch (InvalidOperationException) { } throw; }
        var output = (await stdout).Trim();
        await stderr;
        if (p.ExitCode != 0 || !Path.IsPathFullyQualified(output))
            throw new IOException("A Git workspace is required for reliable conflict detection.");
        var resolved = PhysicalSystemPath(new DirectoryInfo(output).ResolveLinkTarget(true)?.FullName ?? output);
        RejectReparsePoints(resolved);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolved));
    }

    private static string PhysicalSystemPath(string path)
    {
        if (!OperatingSystem.IsMacOS()) return path;
        // macOS owns these fixed root aliases. Resolve only their known protected
        // destinations; user-created project symlinks remain forbidden below.
        foreach (var alias in new[] { "/var", "/tmp", "/etc" })
            if ((path == alias || path.StartsWith(alias + "/", StringComparison.Ordinal)) &&
                new DirectoryInfo(alias).ResolveLinkTarget(true)?.FullName == "/private" + alias)
                return "/private" + path;
        return path;
    }

    private static void RejectReparsePoints(string path)
    {
        for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Use a workspace and Git common directory without symlinks or junctions so repository conflicts can be established reliably.");
    }
}
