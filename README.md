# Codex AutoResume

[![Windows CI](https://img.shields.io/github/actions/workflow/status/arnav42069/codexautoresume/windows.yml?branch=main&label=Windows&logo=github)](https://github.com/arnav42069/codexautoresume/actions/workflows/windows.yml)
[![Linux and macOS CI](https://img.shields.io/github/actions/workflow/status/arnav42069/codexautoresume/platforms.yml?branch=main&label=Linux%20%26%20macOS&logo=github)](https://github.com/arnav42069/codexautoresume/actions/workflows/platforms.yml)
[![Version](https://img.shields.io/github/v/release/arnav42069/codexautoresume?label=version)](https://github.com/arnav42069/codexautoresume/releases/latest)
[![License: MIT](https://img.shields.io/github/license/arnav42069/codexautoresume)](LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-0078D4)](#install)

A desktop app that resumes interrupted Codex work when usage is available again.

- Queue up to five existing conversations.
- Set priorities or run independent repositories in parallel.
- See live status, elapsed time and verified reset estimates.
- Windows: a compact Phosphor dashboard with saved accent colors.
- Keep your queue across restarts. Review uncertain attempts before retrying.

Completed work stays completed. Switching **OFF** stops future continuations while active work finishes.

> Close Codex Desktop while AutoResume owns its sessions. Check your original UUIDs with the [continuity diagnostic](docs/DESKTOP-FEASIBILITY.md) first. AutoResume uses its own app-server, creates no replacement conversations, and cannot guarantee Desktop UI updates.

## Install

Install the Codex CLI and Git, then sign in to Codex.

With Node.js 20.19+:

```sh
npm install -g --foreground-scripts https://github.com/arnav42069/codexautoresume/archive/refs/heads/main.tar.gz
```

Open **Codex AutoResume** from your new desktop shortcut, or run `codexautoresume`.
The installer checks the release checksum. .NET is included.

Prefer a ZIP? Get your platform from [Releases](https://github.com/arnav42069/codexautoresume/releases/latest), extract it somewhere permanent and open the app. First launch creates the shortcut too.

Windows x64 · Linux x64/ARM64 with X11 or XWayland · macOS 12+ on Intel or Apple Silicon.

Codex is detected automatically. Close other Codex clients, confirm ownership, connect and add your verified sessions.

On Windows, closing the window keeps it in the tray by default; change this in settings. On Linux, minimize to keep it running; closing exits once work is settled. macOS also has a menu-bar icon.

## Build

With the .NET 8.0.426 SDK:

```powershell
./scripts/build.ps1
```

On Linux or macOS, use `bash scripts/build-desktop.sh linux-x64` or your matching `linux-arm64`, `osx-x64`, or `osx-arm64` runtime.

[npm commands](docs/NPM-INSTALL.md) · [Usage](docs/USAGE.md) · [Integration](docs/INTEGRATION.md) · [Validation](docs/VALIDATION.md)

[Appearance and automatic Codex detection](docs/PHOSPHOR.md)
