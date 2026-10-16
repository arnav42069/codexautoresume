# Install with npm

On Windows x64, Linux x64/ARM64, or macOS Intel/Apple Silicon, install Node.js 20.19 or later and Git, then run:

```powershell
npm install -g --foreground-scripts https://github.com/arnav42069/codexautoresume/archive/refs/heads/main.tar.gz
```

Use the full source-archive URL. This package is not published on the npm registry.
The installer downloads your platform's pinned release, checks its SHA-256 hash and
extracts it inside the npm package. It creates a desktop shortcut without opening
the UI or submitting Codex work. Open that shortcut or run `codexautoresume`.
The release includes .NET. The native Codex CLI and a signed-in account are still required.

Exit the app before reinstalling or updating. On Windows, use the tray menu. Run the same
install command to get the latest installer from `main`; each installer pins a
specific release rather than trusting a moving download URL.

```powershell
codexautoresume --version
codexautoresume --help
codexautoresume shortcut
npm uninstall -g codexautoresume
```

Uninstall removes the downloaded program; delete its desktop shortcut afterward.
Settings stay in `%LOCALAPPDATA%\CodexAutoResume` on Windows,
`~/.local/state/codexautoresume` on Linux (or `XDG_STATE_HOME`), and
`~/Library/Application Support/CodexAutoResume` on macOS.

Shortcuts are created during installation and first launch. If you delete one,
it stays deleted until you run `codexautoresume shortcut` or use the app’s shortcut button.
Linux also gets an application-menu entry. Some desktops require **Allow Launching**.

## Desktop conversations

Follow the [continuity check](DESKTOP-FEASIBILITY.md) before scheduling existing
Desktop UUIDs. The optional diagnostic downloads only when requested:

```powershell
codexautoresume verify YOUR-UUID --desktop-closed
```

Keep Desktop closed while AutoResume owns those sessions. Backend execution does
not guarantee updates in the Desktop UI. The installer does not change this limit.

## If installation stops

The installer uses curl to show download progress and needs HTTPS access to GitHub Releases. A
failed download or checksum check stops installation; it never runs an unverified
download. Connections time out after 20 seconds; a stalled transfer stops after
45 seconds, and the whole download has a five-minute limit. If your connection
needs longer, use the ZIP link printed by the installer in your browser.
Close the tray app and reinstall if existing program files are damaged.

If your npm policy disables lifecycle scripts, install the payload explicitly:

```powershell
codexautoresume install
```
