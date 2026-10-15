# Codex AutoResume

[![CI](https://img.shields.io/github/actions/workflow/status/arnav42069/codexautoresume/windows.yml?branch=main&label=CI&logo=github)](https://github.com/arnav42069/codexautoresume/actions/workflows/windows.yml)
[![Version](https://img.shields.io/github/v/release/arnav42069/codexautoresume?label=version)](https://github.com/arnav42069/codexautoresume/releases/latest)
[![License: MIT](https://img.shields.io/github/license/arnav42069/codexautoresume)](LICENSE)
[![Platform: Windows x64](https://img.shields.io/badge/platform-Windows%20x64-0078D4)](#install)

A Windows tray app that resumes interrupted Codex work when usage is available again.

- Queue up to five existing conversations.
- Set priorities or run independent repositories in parallel.
- See live status, elapsed time and reset countdowns.
- Keep your queue across restarts. Review uncertain attempts before retrying.

Completed work stays completed. Turning **OFF** stops future continuations without interrupting an active turn.

> **Desktop compatibility is still being verified.** Run the [continuity check](docs/DESKTOP-FEASIBILITY.md) against your original Desktop UUIDs first. Desktop must stay closed while AutoResume owns those sessions. The app uses its own Codex app-server, creates no replacement conversations, and cannot guarantee Desktop UI updates.

## Install

You need Windows 10/11, the native Codex CLI and Git. Sign in to Codex first.

With Node.js 20.19+ on Windows x64:

```powershell
npm install -g --foreground-scripts https://github.com/arnav42069/codexautoresume/archive/refs/heads/main.tar.gz
codexautoresume
```

This downloads the Windows release and checks its SHA-256 hash. No .NET installation needed.
Prefer a ZIP? Download it from [Releases](https://github.com/arnav42069/codexautoresume/releases), extract it and open `CodexAutoResume.exe`.

To uninstall, close the tray app and run `npm uninstall -g codexautoresume`. Your settings stay saved.

## Run

Connect to `codex.exe`, add your verified sessions, set priorities, then switch **ON**.

Closing the window keeps it running in the tray.

[npm commands and uninstall](docs/NPM-INSTALL.md)

## Build

On Windows with the .NET 8.0.426 SDK:

```powershell
./scripts/build.ps1
```

This runs the tests and creates `artifacts/CodexAutoResume-win-x64.zip`. The built app includes .NET.

[Usage](docs/USAGE.md) · [Integration](docs/INTEGRATION.md) · [Validation](docs/VALIDATION.md)
