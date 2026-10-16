using System.Text.Json;

namespace CodexAutoResume.Core;

public static class RecentChats
{
    public static IReadOnlyList<ThreadSnapshot> Order(IEnumerable<ThreadSnapshot> threads) => threads
        .GroupBy(t => Guid.Parse(t.Id)).Select(g => g.OrderByDescending(t => t.VerifiedActive).First())
        .OrderByDescending(t => t.VerifiedActive).ThenByDescending(t => t.RecentActivityAt)
        .Take(5).ToList();
}

public sealed partial class AppServerClient
{
    private static bool Unsupported(RpcException error) => error.DataElement.TryGetProperty("code", out var code) &&
        code.TryGetInt32(out var value) && value is -32601 or -32602;

    public async Task<RecentThreadListing> ListRecentAsync(CancellationToken ct)
    {
        var active = new List<ThreadSnapshot>();
        var activeNote = "Verified active first";
        try
        {
            // Loaded state belongs only to this connected server, never all Desktop clients.
            var loaded = await CallAsync("thread/loaded/list", new { cursor = (string?)null, limit = 100 }, ct);
            if (Protocol.String(loaded, "nextCursor") != null)
                activeNote = "Active ranking limited to the connected server's first 100 loaded chats";
            foreach (var item in loaded.GetProperty("data").EnumerateArray().Take(100))
            {
                var id = item.GetString() ?? throw new InvalidDataException("Loaded chat is missing its UUID.");
                if (!Guid.TryParse(id, out _)) throw new InvalidDataException("Loaded chat has an invalid UUID.");
                try
                {
                    var response = await CallAsync("thread/read", new { threadId = id, includeTurns = false }, ct);
                    var thread = Protocol.Thread(response.GetProperty("thread")); ValidateId(id, thread.Id);
                    if (thread.VerifiedActive) active.Add(thread);
                }
                catch (RpcException) { /* A loaded chat may disappear between read-only calls. */ }
            }
        }
        catch (RpcException ex) when (Unsupported(ex)) { activeNote = "Live activity unavailable on this Codex version"; }

        async Task<List<ThreadSnapshot>> Recent(string key)
        {
            var result = new List<ThreadSnapshot>(); var ids = new HashSet<Guid>();
            var cursors = new HashSet<string>(); string? cursor = null;
            do
            {
                var page = await CallAsync("thread/list", new { cursor, limit = 5, sortKey = key, sortDirection = "desc" }, ct);
                foreach (var item in page.GetProperty("data").EnumerateArray())
                {
                    var thread = Protocol.Thread(item);
                    if (!Guid.TryParse(thread.Id, out var id) || string.IsNullOrWhiteSpace(thread.Workspace))
                        throw new InvalidDataException("Codex returned an invalid existing chat identity or workspace.");
                    if (ids.Add(id)) result.Add(key == "updated_at" ? thread with { RecencyAt = null } : thread);
                }
                cursor = Protocol.String(page, "nextCursor");
                if (cursor != null && !cursors.Add(cursor)) throw new InvalidDataException("Repeated recent-chat pagination cursor.");
                if (cursors.Count > 100) throw new InvalidDataException("Recent-chat pagination exceeded its limit.");
            } while (result.Count < 5 && cursor != null);
            return result;
        }
        List<ThreadSnapshot> recent; string ordering;
        try { recent = await Recent("recency_at"); ordering = "most recent activity"; }
        catch (RpcException ex) when (Unsupported(ex)) { recent = await Recent("updated_at"); ordering = "last updated (recent activity ordering unavailable)"; }
        return new(RecentChats.Order(active.Concat(recent)), $"{activeNote}, then {ordering}. Desktop activity is unknown; this is not Desktop tab order.");
    }
}
