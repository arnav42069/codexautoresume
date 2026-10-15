# Install with npm

On Windows x64, install Node.js 20.19 or later and Git, then run:

```powershell
npm install -g https://github.com/arnav42069/codexautoresume/archive/refs/heads/main.tar.gz
codexautoresume
```

Use the full source-archive URL. This package is not published on the npm registry.
The installer downloads the pinned Windows release, checks its SHA-256 hash and
extracts it inside the npm package. It does not start the app or submit Codex work.
The release includes .NET. The native Codex CLI and a signed-in account are still required.

Close the app from its tray menu before reinstalling or updating. Run the same
install command to get the latest installer from `main`; each installer pins a
specific release rather than trusting a moving download URL.

```powershell
codexautoresume --version
codexautoresume --help
npm uninstall -g codexautoresume
```

Uninstall removes the downloaded program. Your settings stay in
`%LOCALAPPDATA%\CodexAutoResume`.

## Desktop conversations

Follow the [continuity check](DESKTOP-FEASIBILITY.md) before scheduling existing
Desktop UUIDs. The optional diagnostic downloads only when requested:

```powershell
codexautoresume verify YOUR-UUID --desktop-closed --codex "C:\path\codex.exe"
```

Keep Desktop closed while AutoResume owns those sessions. Backend execution does
not guarantee updates in the Desktop UI. The installer does not change this limit.

## If installation stops

The installer needs HTTPS access to GitHub Releases and Windows PowerShell. A
failed download or checksum check stops installation; it never runs an unverified
download. Close the tray app and reinstall if existing program files are damaged.

If your npm policy disables lifecycle scripts, install the payload explicitly:

```powershell
codexautoresume install
```
