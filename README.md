# Codex AutoResume

[![Windows CI](https://img.shields.io/github/actions/workflow/status/arnav42069/codexautoresume/windows.yml?branch=main&label=Windows&logo=github)](https://github.com/arnav42069/codexautoresume/actions/workflows/windows.yml)
[![Linux and macOS CI](https://img.shields.io/github/actions/workflow/status/arnav42069/codexautoresume/platforms.yml?branch=main&label=Linux%20%26%20macOS&logo=github)](https://github.com/arnav42069/codexautoresume/actions/workflows/platforms.yml)
[![Version](https://img.shields.io/github/v/release/arnav42069/codexautoresume?label=version)](https://github.com/arnav42069/codexautoresume/releases/latest)
[![License: MIT](https://img.shields.io/github/license/arnav42069/codexautoresume)](LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-0078D4)](#install)

A desktop app that resumes interrupted Codex work when usage is available again.

![Read-only connection with a sample recent chat](https://github.com/arnav42069/codexautoresume/releases/download/v0.3.0/CodexAutoResume-Phosphor-readonly.png)

- Connect with Codex Desktop open and discover your five recent chats.
- Keep your queue and priorities across refreshes.
- Set priorities or run independent repositories in parallel.
- See live status, elapsed time and verified reset estimates.
- Windows: a Phosphor dashboard with saved colors and reduced motion. The pink is a preview.
- Keep your queue across restarts. Review uncertain attempts before retrying.
- Reconnect automatically if Codex starts later, the CLI is updated, or the app-server drops. The queue is preserved and scheduling stays OFF until ownership is confirmed again.

Completed work stays completed. Switching **OFF** stops future continuations while active work finishes.

> Desktop may stay open for browsing. Close it before handing execution ownership to AutoResume. Check your original UUIDs with the [continuity diagnostic](docs/DESKTOP-FEASIBILITY.md) first. AutoResume uses its own app-server, creates no replacement conversations, and cannot guarantee Desktop UI updates.

## Install

Install the Codex CLI and Git, then sign in to Codex.

With Node.js 20.19+:

```sh
npm install -g --foreground-scripts https://github.com/arnav42069/codexautoresume/archive/refs/heads/main.tar.gz
```

Open **Codex AutoResume** from your new desktop shortcut, or run `codexautoresume`.
The installer checks the release checksum. .NET is included.

Prefer a ZIP? Download v0.3.0, extract it somewhere permanent and open the app. No Node.js or .NET installation is needed for the ZIP. First launch creates the shortcut.

| Platform | Download |
| --- | --- |
| Windows | [x64](https://github.com/arnav42069/codexautoresume/releases/download/v0.3.0/CodexAutoResume-win-x64.zip) |
| Linux | [x64](https://github.com/arnav42069/codexautoresume/releases/download/v0.3.0/CodexAutoResume-linux-x64.zip) · [ARM64](https://github.com/arnav42069/codexautoresume/releases/download/v0.3.0/CodexAutoResume-linux-arm64.zip) |
| macOS | [Intel](https://github.com/arnav42069/codexautoresume/releases/download/v0.3.0/CodexAutoResume-osx-x64.zip) · [Apple Silicon](https://github.com/arnav42069/codexautoresume/releases/download/v0.3.0/CodexAutoResume-osx-arm64.zip) |

Linux needs X11 or XWayland. macOS needs version 12+; builds are ad-hoc signed, not notarized.

1. Open AutoResume. It detects Codex, connects read-only and fills an empty queue with up to five recent chats. Desktop may stay open.
2. Review the queue. Verified active chats come first; other Desktop activity is labeled unknown. Your existing priorities stay fixed.
3. To resume work, finish active Codex work, close other clients, confirm ownership and choose **Acquire execution ownership**, then switch **ON**.

**ON** controls scheduling. The watcher panel shows whether the connection is actually ready.

On Windows, closing the window keeps it in the tray by default; change this in settings. On Linux, minimize to keep it running; closing exits once work is settled. macOS also has a menu-bar icon.

## Build

With the .NET 8.0.426 SDK:

```powershell
./scripts/build.ps1
```

On Linux or macOS, use `bash scripts/build-desktop.sh linux-x64` or your matching `linux-arm64`, `osx-x64`, or `osx-arm64` runtime.

[npm commands](docs/NPM-INSTALL.md) · [Usage](docs/USAGE.md) · [Integration](docs/INTEGRATION.md) · [Validation](docs/VALIDATION.md)

[Desktop-open discovery](docs/DISCOVERY.md) · [Appearance and Codex detection](docs/PHOSPHOR.md)
