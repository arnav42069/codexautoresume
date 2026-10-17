# Validation record

Validated on the Linux development host on 2026-10-14 using .NET SDK 8.0.426 and codex-cli 0.159.2.

| Check | Result |
| --- | --- |
| Automated policy, persistence, Git and transport suite | 45 passed, 0 failed; 45 executed |
| Priority/sequential and starred parallel execution | Passed with deterministic fake execution |
| Repository conflicts | Passed, including actual Git worktrees sharing one common directory |
| Limit recovery, reset verification, unknown-reset backoff | Passed with structured simulated usage and deterministic time |
| UUID mismatch and workspace drift | Passed; dispatch blocked |
| Durable intent before dispatch, crash uncertainty, duplicates | Passed |
| Independent events, stale completions, scoped reconciliation errors | Passed |
| Approval denial and transient error handling | Passed |
| Real subprocess JSON-RPC fixture | Passed framing, pagination, errors, identity targeting and serialized denial |
| Actual installed app-server | Initialization and paginated discovery passed; 0 sessions returned; no turns submitted |
| Release solution build | Passed with 0 warnings and 0 errors |

The automated entry point is `dotnet run --project tests/CodexAutoResume.Tests -c Release`. Tests use a dependency-free executable runner with a nonzero failure exit status. They do not call a model. A separate `--live-smoke <native-codex-path>` command performs only initialization and discovery against the real app-server.

The Windows artifact is cross-published from Linux as a self-contained, compressed single-file win-x64 executable. The runtime artifact is unsigned. SHA256SUMS.txt beside the release ZIP records the delivered executable/archive hashes.

## Existing Desktop conversation follow-up — 2026-10-15

The expanded automated suite executed **55 tests: 55 passed, 0 failed**. Ten additional subprocess cases cover exact UUID/context preservation, full history pagination, unknown UUID rejection without fallback, active/unfinished threads, empty context, changed UUID/history/workspace, and loss of exclusive ownership. These cases use synthetic protocol fixtures, not Desktop conversations. The complete solution, including the standalone probe, builds without warnings or errors.

A repeated real app-server initialization/discovery check returned **0 sessions** on this Linux host. It sent no continuation and created no thread. Thus the feasibility check for the user's original Windows Desktop conversations remains **not verified**, regardless of passing generic tests. The standalone diagnostic must run against the exact original UUIDs on their Windows machine before proceeding with Desktop-session execution. See [the feasibility gate](DESKTOP-FEASIBILITY.md).

The [first Windows CI run](https://github.com/arnav42069/codexautoresume/actions/runs/38688725834) passed all 55 tests, built the self-contained executable, and passed its native WPF/tray startup check on 2026-10-15. Manual rendering and interaction, Windows login startup, authenticated model continuation, actual usage-limit resets, and Desktop history/UI visibility remain unverified. Follow [the Windows acceptance checklist](WINDOWS-VALIDATION.md) for those checks. Direct Desktop control and synchronization remain unsupported.

## Phosphor and discovery update

Windows CI run [38886643498](https://github.com/arnav42069/codexautoresume/actions/runs/38886643498) passed the native WPF render/connection checks and official Codex CLI 0.163.0 automatic discovery in an isolated Codex home. The WPF check rendered empty, cyan/reduced-motion, connection-error, connected/OFF, five-conversation and narrow-window states, using a local protocol fixture without model turns. Screenshots are attached to that CI run. Core tests also verify reopening persisted appearance and executable settings in a new scheduler, with scheduling OFF.

Linux local checks passed 75 core tests, 38 npm tests (two Windows-specific tests skipped), a warning-free WPF cross-build, real native Linux window rendering, and automatic discovery/initialization/listing with Codex CLI 0.159.2. No model turns were submitted. Physical Windows DPI/accessibility behavior and the user's original Desktop conversation history still require the acceptance checks; these results do not establish Desktop UI synchronization.


The v0.2.1 native render review corrected the WPF main-window style lookup and added assertions for the actual background and foreground. v0.2.2 also routes UI error refreshes through existing safe presentation snapshots, without changing scheduling. Final release validation: [Windows, Linux and macOS](https://github.com/arnav42069/codexautoresume/actions/runs/38887852219).

## Desktop-open discovery (v0.3.0)

Local verification passed 89 core tests and the packaged Linux window/shortcut check, including native fixture discovery, automatic unarmed queue import, recency sorting/fallback, pagination, stale activity clearing, workspace drift and queue/journal preservation. Real Codex CLI 0.159.2 recent-chat queries completed read-only with zero sessions visible on this host; this does not establish access to the user's Desktop history.

[Windows run 38957292850](https://github.com/arnav42069/codexautoresume/actions/runs/38957292850) passed native WPF checks with the ownership checkbox unticked during connection, plus official CLI discovery and read-only protocol validation. [Linux/macOS checks](https://github.com/arnav42069/codexautoresume/actions/runs/38957292928) exercise the same native fixture connection through the packaged interfaces. No model turn is submitted by these checks.

[Release validation 38957769813](https://github.com/arnav42069/codexautoresume/actions/runs/38957769813) passed all five native targets for v0.3.0 and verified every public application/diagnostic ZIP against its checksum. The Windows read-only screenshot was inspected after publication; connection remained established with the execution checkbox unticked and scheduling OFF.

## Automatic reconnect

The reconnect update adds coverage for a later CLI discovery retry and for stale events from a retired server generation. The final local core run completed **91 tests: 91 passed, 0 failed**. The cross-platform desktop and Windows WPF projects built with **0 warnings and 0 errors**. The npm launcher suite completed **38 passed, 0 failed, 2 skipped**.
