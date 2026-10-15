# Windows acceptance checks

These checks require Windows and a disposable local Git workspace with the user's authenticated native Codex CLI. They were not run on this Linux host. Use harmless tasks and review the existing Codex configuration before permitting real execution.

1. Run `scripts/build.ps1`. Confirm automated tests and native WPF/tray startup (`--self-test`) succeed. The startup check does not connect to Codex.
2. Launch the published executable. Check keyboard navigation, six badge colors, running pulse, summary and log at 100% and 150% scaling. Minimize/close to tray and restore. Notification policy may suppress balloons.
3. Close other clients, connect to native Codex and add a known disposable session through discovery and UUID entry. Confirm title/path. Random/duplicate UUIDs, a missing workspace and a sixth entry must fail without sending prompts.
4. Enable an interrupted harmless task. Verify one configured continuation reaches its exact thread, elapsed time advances, completion records a timestamp and disarms further messages. Explicit rearm permits one new turn. Keep Desktop closed.
5. Use independent repositories for starred parallel execution and the sequential lane. Reorder waiting rows and pause one. Same-repository sessions and shared Git worktrees must serialize.
6. Turn OFF during a run: active execution continues, future dispatch stops. Tray pause/resume behaves identically. Exit refuses while execution or delivery is uncertain.
7. At a naturally occurring usage limit, check terminal identity, waiting account work, reset/countdown and fresh ordinary-usage verification before any continuation. Do not spend credits to manufacture a limit. Unknown resets show backoff.
8. Trigger a legitimate approval/input request. Verify no permissions are granted and attention stays latched. Resolve in a supported client while AutoResume is OFF; close that client, restart and explicitly rearm.
9. Test unexpected termination only with disposable work. Restart OFF, reconcile known turn IDs, and require inspection for unacknowledged delivery. Never blindly rearm uncertain work.
10. Enable Windows startup and reboot after work settles. Confirm tray launch, restored queue and scheduling OFF. Reconnect/revalidate before enabling. Disable startup and verify removal of the HKCU entry.

Record Windows/Codex versions and actual outcomes. Desktop synchronization is outside the supported scope and cannot be guaranteed by this implementation.
