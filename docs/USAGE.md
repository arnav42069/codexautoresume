# Using Codex AutoResume

A compact .NET 8 desktop utility for Windows, Linux and macOS for continuing up to five interrupted Codex sessions, with a durable priority queue and live conversation status.

**Desktop integration limit:** this utility controls an app-server it starts. It does **not** control the standalone Codex Desktop application, attach to its private server, or promise that resumed sessions appear updated in Desktop. Shared local history is not a cross-process execution lock. Close Desktop and all other Codex clients, confirm exclusive ownership, and keep them closed while AutoResume owns a session. Direct Desktop control remains unsupported. See [integration findings and architecture](INTEGRATION.md).

**Existing Desktop conversation feasibility is still pending.** The earlier build and generic app-server tests did not establish that your Windows Desktop UUIDs and context are accessible. Keep scheduling OFF for those sessions until the [existing-conversation diagnostic](DESKTOP-FEASIBILITY.md) passes on the machine containing their history. It only resumes the supplied existing UUID to inspect history; it never starts a model turn or creates/forks a replacement thread.

## Install and run

1. Extract your platform ZIP into a stable user location. Open `CodexAutoResume.exe` on Windows, `CodexAutoResume` on Linux, or `CodexAutoResume.app` on macOS. First launch creates a desktop shortcut. The executable is self-contained; .NET does not need to be installed. This build is unsigned. The native runtime extracts to the user's temporary directory on first launch.
2. Install the native Codex CLI and Git. Protocol validation used **codex-cli 0.159.2**; older versions may lack required fields and fail closed. Sign in through Codex's normal supported flow. AutoResume never reads or stores authentication files or keys.
3. Launch AutoResume. It finds the native CLI automatically, including npm installs and Unix symlinks. Use **Find Codex** to search again or **Browse** for a custom installation. Close Desktop and other Codex clients, confirm exclusive ownership, then connect. The server inherits your existing environment and Codex configuration, including `CODEX_HOME` when set.
4. Confirm exclusive ownership and click **Connect**. This starts `codex app-server --listen stdio://`, initializes the protocol, and revalidates saved sessions. No continuation is sent during connection.
5. Click **+ Add conversation**. Select a session returned by `thread/list` or enter its exact UUID. Confirm the title, UUID and workspace. Only sessions accessible to that Codex installation/account/home are discoverable. Sessions require an existing local Git workspace without symlinks or junctions. Identity mismatches, changed workspaces and missing repositories block execution.
6. Arrange priority with ↑ / ↓. **☆ Sequential** uses the single sequential lane; **★ Parallel** permits concurrent execution in independent repositories. Conflicting repositories, including worktrees sharing a Git common directory, and nested workspaces are serialized. An active execution's lane cannot change.
7. Review the continuation prompt, then turn scheduling **ON**. Interrupted work is eligible; completed and empty sessions stay disarmed. **Retry / rearm** explicitly permits one further continuation after inspecting the session. It can intentionally continue completed work.

AutoResume checks `ordinaryUsageAllowed` and exhausted usage windows before scheduling. A reset estimate is not authorization. Older APIs, API-key accounts, or backends without affirmative ordinary-usage permission cannot recover automatically; the row explains the unavailable permission. AutoResume never calls the reset-credit consumption endpoint. Codex enforces usage and billing according to the user's own configuration.

Default continuation prompt:

> Continue the previously interrupted task from where you stopped. Inspect the current work and conversation context, avoid repeating completed steps, and finish the remaining tasks. Do not start unrelated work.

## Live status

| Badge | Meaning |
| --- | --- |
| Blue · Running | Live execution, elapsed time and a subtle pulse |
| Purple · Queued | Armed and waiting; position in priority order |
| Amber · Rate Limited | Estimated reset, countdown and next usage verification |
| Green · Completed | Completion timestamp saved; further automatic continuation disarmed |
| Red · Needs Attention | Intervention reason, approval, failure, uncertain delivery or unavailable state |
| Gray · Paused | Future scheduling disabled, or no work armed |

The header counts running, queued, rate-limited and completed conversations. Each conversation has independent turn identity, timestamps, reset estimate and attempt journal. Account-wide limits conservatively hold eligible waiting work without changing running or completed conversations.

Restored badges are marked **unverified** until Codex state is read again. If the server omits a completion timestamp, the UI labels the recorded time **Completion observed**; an unavailable execution start time is reported rather than invented.

Supported events update state immediately. Read-only reconciliation runs every 30 seconds with a five-second scheduler heartbeat. Display clocks update every second without contacting Codex. Unknown reset times back off from 15 to 30 to 60 minutes. Known resets are verified after a five-second grace period. Error notifications alone do not release running work; a terminal event or reconciliation must establish its outcome.

## Tray, reboot and recovery

- Closing or minimizing hides the window to the tray; the watcher continues. Tray actions open the window, pause/resume scheduling, or exit. Notifications report resumes, limits, completions and intervention.
- Turning **OFF**, or pausing a row, stops future dispatches without interrupting active work. Explicit exit is blocked while work is active or journal delivery is uncertain. OS termination is treated as a possible crash on restart.
- **Start with Windows** registers the current executable in the current user's `Run` registry key with `--tray`. Uncheck it before moving/deleting the executable. Queue, prompts, statuses and preferences survive reboot; scheduling starts OFF and requires connection and exclusive-ownership confirmation again. This prevents unattended takeover of Desktop sessions after reboot.
- A successful completion never triggers another prompt automatically. Approval/input requests are declined or rejected; AutoResume grants no permissions and does not change sandbox policy. The model may finish its existing turn after a denial; the row remains Needs Attention until reviewed.
- Resolve an approval or uncertain history in a supported Codex client while AutoResume is OFF. Close that client, restart/reconnect AutoResume, then explicitly rearm. AutoResume does not provide an approval UI.
- Attempts have unique IDs journaled **before** `turn/start`. The message ID is for correlation; server-side idempotency is not assumed. After a crash, known accepted turn IDs are reconciled. Uncertain dispatches are never replayed automatically, even if later history looks complete. Inspect history and explicitly acknowledge reconciliation before rearming. Avoiding duplicates takes priority over recovering automatically from every crash.
- Data lives in `%LOCALAPPDATA%\CodexAutoResume\state.json`, with the previous atomic snapshot in `state.json.bak`. Files contain paths, prompts and journal metadata; protect them like other local development data. Corrupt/unsupported state stops startup. Restore a known-good backup manually after inspection; older backups may require additional reconciliation.
- A single-instance mutex and exclusive data-file lock prevent concurrent AutoResume instances, including separate Windows login sessions sharing the same data. A best-effort process-name check stops scheduling if another `Codex` process appears. It cannot detect every wrapper, remote client or Desktop version. Exclusive ownership remains essential because the protocol provides no global compare-and-start or cross-client lease.

## Build and test

Install .NET SDK 8.0.426 or a compatible newer .NET 8 SDK. From the repository root:

```powershell
dotnet build CodexAutoResume.sln -c Release
dotnet run --project tests/CodexAutoResume.Tests -c Release
./scripts/build.ps1
```

The dependency-free executable test runner prints each test and returns nonzero on failure. `dotnet test` is not the entry point. It covers scheduling, conflicts, rate recovery/backoff, identity, journaling, crash uncertainty, duplicates, event isolation, approvals, persistence and a real subprocess JSON-RPC fixture.

`scripts/build.ps1` publishes a self-contained single-file `win-x64` executable, checks native WPF/tray startup on Windows using `--self-test`, and produces `artifacts/CodexAutoResume-win-x64.zip`. Use `-Runtime win-arm64` on a matching Windows ARM64 machine. The UI startup check never connects to Codex or submits a prompt. GitHub Actions runs the Windows build, tests and startup check and retains the ZIP.

Linux can run the portable core/tests and cross-publish Windows:

```sh
dotnet run --project tests/CodexAutoResume.Tests -c Release
dotnet publish src/CodexAutoResume.Windows -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
  -o artifacts/win-x64
```

Optional read-only real app-server initialization/discovery, and local schema verification:

```powershell
dotnet run --project tests/CodexAutoResume.Tests -c Release -- --live-smoke C:\path\to\codex.exe
./scripts/verify-codex.ps1 -Codex C:\path\to\codex.exe
```

## Validation boundary

This development host is Linux. Core/transport tests, read-only app-server initialization/discovery, compilation and Windows cross-publication are checked here. WPF rendering, tray behavior, Windows startup, authenticated resumptions, real resets and Desktop visibility require Windows/user-account validation and are not claimed to work based on these checks. No real continuation prompts were submitted. See [Windows acceptance checks](WINDOWS-VALIDATION.md).

## Platform behavior

Windows retains its WPF tray interface. Linux and macOS use a native Avalonia interface with the same scheduler, queue journal, status presentation and exact-UUID safeguards. Linux needs X11 or XWayland. Minimize the Linux window to keep it running; closing it exits only after active/uncertain work is settled. macOS has a menu-bar icon and an explicit hide action.

Use **Desktop shortcut** or `codexautoresume shortcut` to repair an icon. Deleting a shortcut keeps it deleted for the same installation. npm uninstall leaves user settings and desktop icons; remove the icon manually. Start at login is optional and uses the Windows Run key, XDG autostart, or a macOS LaunchAgent.

For isolated tests, `AUTORESUME_DATA_HOME` and `AUTORESUME_DESKTOP_DIR` can point to temporary directories. These do not change Codex authentication or its history home.
