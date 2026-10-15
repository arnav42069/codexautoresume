# Existing Desktop conversations: feasibility gate

## Current conclusion

**Not yet verified for the user's Windows Desktop conversations.** The installed app-server protocol supports loading an existing stored UUID with recorded history. That is conditional protocol support, not proof that any particular Desktop conversation is stored in a compatible home/backend or that another process is idle.

This investigation runs on Linux with codex-cli 0.159.2. Windows Desktop and its session store are not available here. Generic mock tests, a successful app-server handshake, and an empty discovery result do not pass the Desktop feasibility gate. The previously built Windows app must remain OFF for these sessions until target-machine validation succeeds. No separate replacement-session implementation is being substituted.

The installed generated `ThreadResumeParams` description states:

> By thread_id: load the thread from disk by thread_id and resume it.

It also describes history/path overrides that can take precedence over the requested identity. The diagnostic and application use **only `threadId`** for identity, without history/path overrides. `thread/resume` is distinct from `thread/start` and `thread/fork`; the client contains no create/fork fallback. A missing UUID or mismatched response is a failure.

## Check the real Windows sessions without sending a prompt

1. Let active Desktop work finish, or resolve it manually in Desktop. Record the original UUID(s) of one to five intended Desktop conversations. Do not create test replacements and do not use `--last`.
2. Close Desktop, AutoResume, and other Codex clients completely. Keep them closed throughout the diagnostic. Closing a window must not leave its process/background work running. Do not terminate active work just to make this check pass.
3. Use the native `codex.exe` and existing authentication/configuration/home that can access those conversations. The tool does not copy credentials, edit configuration, inspect private Desktop databases, or override `CODEX_HOME`. If the selected CLI cannot access that history, the check fails rather than trying another identity or making a new thread.
4. Extract the self-contained Windows diagnostic and run in PowerShell, replacing the executable path and UUIDs:

```powershell
./CodexAutoResume.Probe.exe --codex C:\path\to\codex.exe --desktop-closed ORIGINAL-DESKTOP-UUID --output desktop-continuity.json
```

Add up to four more original UUIDs before `--output`. Existing report files are never overwritten; select another filename for subsequent checks. Git and a .NET installation are not required for the self-contained diagnostic.

The source can also run with the .NET 8 SDK:

```powershell
dotnet run --project tools/CodexAutoResume.Probe -c Release -- --codex C:\path\to\codex.exe --desktop-closed ORIGINAL-DESKTOP-UUID --output desktop-continuity.json
```

The diagnostic is intentionally Windows-only at its entry point. Its portable core is tested using subprocess fixtures; fixture success is not reported as real Desktop success.

To reproduce the self-contained diagnostic from source (Windows or Linux with the .NET 8 SDK):

```powershell
dotnet publish tools/CodexAutoResume.Probe -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/desktop-probe-win-x64
```

## What the diagnostic establishes

For every supplied UUID it:

1. Reads the exact thread and requires an inactive, known state.
2. Paginates all visible turns and items. An unfinished turn, empty/inaccessible history, duplicate identities, or incomplete pagination fails the check.
3. Resumes **that UUID only**, checks that the returned UUID and workspace match, and requires idle state. There is no turn submission and no new-thread fallback.
4. Reads the recorded history again and compares an ordered SHA-256 fingerprint of turn identities/statuses and complete API-visible item content. Object property order is canonicalized; observational wrapper timestamps are excluded. Context is processed in memory and is not printed or saved in the report.
5. Writes a local JSON report containing IDs, workspace, counts, hashes and method names. The caller identifies these UUIDs as Desktop conversations; the tool cannot independently prove Desktop provenance through the UI.

Only `initialize`, `thread/read`, `thread/turns/list`, `thread/items/list`, and `thread/resume` are used. No `turn/start`, `thread/start`, `thread/fork`, usage-credit operation, model prompt, or approval grant is sent. Resume can load/update server-side session metadata, so this is a **no-prompt hydration diagnostic**, not a promise of zero filesystem side effects. Any unexpected incoming server request cancels the check.

Exit 0 means backend identity and API-visible recorded history were preserved for every supplied UUID. Exit 1 means at least one check failed; exit 2 means the diagnostic could not be run or its report could not be saved. A failed UUID stays ineligible: no replacement is created. Review the report before sharing it; it includes local workspace paths and session identities, though it omits message contents and credentials.

## Separate limitations that remain even after a successful check

| Question | What can be established |
| --- | --- |
| Original backend identity | Requested UUID equals resumed UUID; no create/fork fallback |
| Recorded context | Same full API-visible history before/after hydration; this does not prove model use of every item, ephemeral UI state, or unavailable cloud context |
| Backend execution | Existing owned app-server turns can target the verified UUID; actual authenticated execution is a subsequent check |
| Desktop UI synchronization | Not established by backend success; updates may be absent, delayed, or require reloading |
| Competing Desktop turns | No global cross-process lease or atomic idle-and-start operation was verified. A separate server's idle status does not prove Desktop is idle. Fully closed Desktop and exclusive handoff remain prerequisites |

The best-effort process check is an extra guard. It cannot guarantee safety against every renamed client or a client opened at the instant a turn starts. Therefore AutoResume cannot promise competition-free execution while Desktop is open or being used concurrently. If keeping Desktop open is required, the verified interfaces do not satisfy that requirement and automatic execution must stay disabled.

## Next decision

Return the diagnostic report from the Windows machine. If the intended UUIDs and recorded history are preserved and exclusive handoff is acceptable, the existing queue/status/journal implementation can be validated against those exact sessions. If access, identity or history fails, report that limitation and stop; do not implement or silently substitute independent conversations. The earlier generic test results alone are not sufficient to proceed.
