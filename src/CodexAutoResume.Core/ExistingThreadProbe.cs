using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace CodexAutoResume.Core;

public sealed record ExistingThreadEvidence(string RequestedId, string ResumedId, string Workspace,
    int Turns, int Items, string HistoryBeforeSha256, string HistoryAfterSha256,
    IReadOnlyList<string> MethodsInvoked, DateTimeOffset VerifiedAt);

public sealed partial class AppServerClient
{
    /// <summary>
    /// Hydrates an existing stored UUID and compares its complete API-visible history.
    /// Sends no turn, creates no thread, and provides no Desktop cross-process lease.
    /// The caller must establish exclusive access independently before connecting.
    /// </summary>
    public async Task<ExistingThreadEvidence> VerifyExistingThreadAsync(string id, Action ensureExclusive,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var uuid)) throw new ArgumentException("An exact existing session UUID is required.");
        id = uuid.ToString();
        var methods = new List<string>();

        async Task<JsonElement> ProbeCall(string method, object parameters)
        {
            if (method is not ("thread/read" or "thread/turns/list" or "thread/items/list" or "thread/resume"))
                throw new InvalidOperationException("The continuity probe cannot create threads or submit turns.");
            ensureExclusive();
            ct.ThrowIfCancellationRequested();
            methods.Add(method);
            var result = await CallAsync(method, parameters, ct);
            ensureExclusive();
            return result;
        }

        async Task<ThreadSnapshot> ReadMetadata()
        {
            var result = await ProbeCall("thread/read", new { threadId = id, includeTurns = false });
            var thread = Protocol.Thread(result.GetProperty("thread"));
            ValidateId(id, thread.Id);
            EnsureInactive(thread);
            return thread;
        }

        async Task<(string Hash, int Turns, int Items)> ReadHistory()
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var turnIds = new HashSet<string>(StringComparer.Ordinal);
            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            var items = 0;
            async IAsyncEnumerable<JsonElement> Pages(string method)
            {
                string? cursor = null;
                var cursors = new HashSet<string>(StringComparer.Ordinal);
                var pages = 0;
                do
                {
                    if (++pages > 1000) throw new InvalidDataException("History exceeds the diagnostic page limit; verification is incomplete.");
                    object parameters = method == "thread/turns/list"
                        ? new { threadId = id, cursor, limit = 100, sortDirection = "asc", itemsView = "summary" }
                        : new { threadId = id, cursor, limit = 100, sortDirection = "asc" };
                    var response = await ProbeCall(method, parameters);
                    foreach (var entry in response.GetProperty("data").EnumerateArray()) yield return entry;
                    cursor = Protocol.String(response, "nextCursor");
                    if (cursor != null && !cursors.Add(cursor)) throw new InvalidDataException("Repeated history cursor; verification is incomplete.");
                } while (cursor != null);
            }

            Append(hash, JsonSerializer.SerializeToElement("turns"));
            await foreach (var turn in Pages("thread/turns/list"))
            {
                var turnId = Protocol.String(turn, "id") ?? throw new InvalidDataException("A stored turn has no identity.");
                var status = Protocol.String(turn, "status");
                if (status == "inProgress") throw new InvalidOperationException("History contains unfinished execution. Resolve it in Codex before transferring ownership.");
                if (status is not ("completed" or "interrupted" or "failed")) throw new InvalidDataException("Unsupported stored turn status.");
                if (!turnIds.Add(turnId)) throw new InvalidDataException("Duplicate turn identity in paginated history.");
                Append(hash, JsonSerializer.SerializeToElement(new { id = turnId, status }));
            }
            Append(hash, JsonSerializer.SerializeToElement("items"));
            await foreach (var entry in Pages("thread/items/list"))
            {
                var turnId = Protocol.String(entry, "turnId");
                var item = entry.GetProperty("item");
                var itemId = Protocol.String(item, "id") ?? throw new InvalidDataException("A stored history item has no identity.");
                if (turnId == null || !turnIds.Contains(turnId)) throw new InvalidDataException("An item does not belong to the verified turn history.");
                if (!itemIds.Add(itemId)) throw new InvalidDataException("Duplicate item identity in paginated history.");
                // Item wrapper timestamps are observational metadata, not conversation context.
                Append(hash, JsonSerializer.SerializeToElement(new { turnId, item }));
                items++;
            }
            if (turnIds.Count == 0 || items == 0) throw new InvalidDataException("No nonempty recorded conversation context could be verified. No thread will be created.");
            return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), turnIds.Count, items);
        }

        var before = await ReadMetadata();
        var historyBefore = await ReadHistory();
        // Only the original ID is supplied. Never supply history/path, which could
        // change identity precedence, or fall back to thread/start or thread/fork.
        var resumed = await ProbeCall("thread/resume", new { threadId = id, excludeTurns = true });
        var returned = Protocol.Thread(resumed.GetProperty("thread"));
        ValidateId(id, returned.Id);
        EnsureInactive(returned);
        if (returned.RuntimeStatus != "idle") throw new InvalidOperationException("The server did not establish an idle resumed thread.");
        var after = await ReadMetadata();
        if (!Path.GetFullPath(before.Workspace).Equals(Path.GetFullPath(returned.Workspace), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(before.Workspace).Equals(Path.GetFullPath(after.Workspace), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The workspace changed while resuming the selected UUID.");
        var historyAfter = await ReadHistory();
        if (historyBefore != historyAfter) throw new InvalidDataException("Recorded history changed during resume. Context continuity was not verified; no continuation was sent.");
        ensureExclusive();
        return new(id, returned.Id, returned.Workspace, historyBefore.Turns, historyBefore.Items,
            historyBefore.Hash, historyAfter.Hash, methods.AsReadOnly(), DateTimeOffset.UtcNow);
    }

    private static void EnsureInactive(ThreadSnapshot thread)
    {
        if (thread.RuntimeStatus is not ("idle" or "notLoaded") || thread.ActiveFlags.Length != 0)
            throw new InvalidOperationException("The selected thread is active, needs intervention, or has an unknown state. No resume or continuation is permitted.");
    }

    private static void Append(IncrementalHash hash, JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Canonical(writer, value);
        var bytes = stream.ToArray();
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void Canonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); Canonical(writer, property.Value); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Canonical(writer, item); writer.WriteEndArray(); break;
            default: value.WriteTo(writer); break;
        }
    }
}
