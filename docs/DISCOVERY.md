# Connect while Desktop stays open

AutoResume detects the Codex CLI and connects automatically in **read-only mode**. Codex Desktop may remain open. The connection lists existing chats and reads their metadata/history; it never resumes a thread, starts a turn, creates a replacement conversation or checks usage by sending a prompt.

An empty queue gets up to five chats. Verified active chats on the connected server come first, followed by backend recent activity. Existing queues keep their order, prompts, parallel flags and attempt journal. Removing every row does not immediately refill the queue in the same app session. You can also select a recent chat with **Add conversation**.

The list uses `recency_at`, descending. Older compatible backends can fall back to `updated_at`, labeled **last updated**. It is not the order of Desktop tabs or recently viewed windows. Only the chosen Codex home/backend's accessible history can be listed. Missing history remains missing; AutoResume never substitutes a new UUID.

Live status belongs to the server answering the request. An independent app-server cannot reliably see activity owned by Desktop. Those rows say **Activity unknown**. A last recorded completed or interrupted turn is historical evidence, not proof of global idleness. Known active state clears on restart, disconnect or a failed observation. The server's loaded-chat query is bounded to 100 entries and reports that limit when reached. No screen scraping or private Desktop endpoint is used.

Observation refreshes every 30 seconds and on supported events. **Refresh chats** retries immediately without replacing queue priorities. Failure to list recent chats does not prevent reading the already-selected UUIDs or reconciling AutoResume's existing attempts.

## Reconnection

AutoResume keeps one owned app-server child per connection. If that child exits or its JSON-RPC stream breaks, the utility marks the watcher disconnected, preserves the queue and durable attempt journal, and retries discovery with bounded backoff. A saved CLI path is revalidated on every retry, so an updated npm installation can be found without restarting AutoResume. Concurrent retry requests are coalesced and an old connection's events cannot affect the replacement connection.

Recovery is read-only. It never reacquires execution ownership, resumes a conversation, or resends an uncertain message. Finish active work and close other Codex clients before explicitly acquiring ownership again. A healthy existing connection is not replaced just because a different executable path is entered.

## Hand off execution

1. Finish active work, close Desktop and other Codex clients.
2. Tick the execution ownership checkbox and choose **Acquire execution ownership**.
3. Review the revalidated queue, then switch **ON**.

Handoff checks the original UUIDs, workspaces and history again. Completed work stays disarmed. Interrupted work becomes eligible only after revalidation; uncertain delivery still requires inspection and explicit reconciliation. The handoff itself sends no continuation.

If another Codex client appears, future scheduling stops and the connection switches back to read-only. Already running AutoResume turns are not interrupted, and their completion can still be reconciled. Closing the app remains blocked while its execution or delivery is uncertain.

Concurrent automatic continuation while Desktop is used remains unsupported: the verified API has no atomic idle-and-start guard or shared execution lease, and `turn/start` may steer an active turn. A shared socket or polling an idle status does not solve that race. Backend execution does not guarantee Desktop UI synchronization.
