using System.Text.Json;

namespace CodexAutoResume.Tests;

// A real child process exercises the newline JSON transport, not ICodexClient mocks.
internal static class ProtocolFixture
{
    public const string Id = "00000000-0000-4000-8000-000000000001";
    public static async Task Run()
    {
        string turnId = "old-turn", turnStatus = "interrupted";
        var mode = Environment.GetEnvironmentVariable("AUTORESUME_PROBE_FIXTURE");
        if (mode == "unfinished") turnStatus = "inProgress";
        int resumes = 0;
        object Thread(string? overrideId = null) => new { id = overrideId ?? Id, name = "Fixture",
            cwd = mode == "workspace-change" && resumes > 0 ? "/other" : "/fixture",
            status = new { type = mode == "active" ? "active" : "idle" }, preview = "Fixture preview" };
        object Turn() => new { id = turnId, status = turnStatus };
        void Send(object value) { Console.WriteLine(JsonSerializer.Serialize(value)); Console.Out.Flush(); }
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("method", out var method))
            {
                // Confirm the actual client serialized a denial, never acceptance.
                if (root.GetProperty("result").GetProperty("decision").GetString() != "decline") Environment.Exit(8);
                Send(new { method = "fixture/rejected", @params = new { } }); continue;
            }
            if (!root.TryGetProperty("id", out var requestId)) continue;
            var id = requestId.Clone(); var p = root.GetProperty("params");
            Send(new { method = "fixture/method", @params = new { method = method.GetString() } });
            if (p.TryGetProperty("threadId", out var requested) && requested.GetString() != Id)
            { Send(new { id, error = new { code = -32600, message = "Unknown UUID" } }); continue; }
            object result;
            switch (method.GetString())
            {
                case "initialize": result = new { userAgent = "fixture" }; break;
                case "thread/list":
                    result = p.GetProperty("cursor").ValueKind == JsonValueKind.Null
                        ? new { data = new[] { Thread() }, nextCursor = (string?)"second" }
                        : new { data = Array.Empty<object>(), nextCursor = (string?)null }; break;
                case "thread/read": result = new { thread = Thread() }; break;
                case "thread/resume":
                    if (p.EnumerateObject().Any(prop => prop.Name is not ("threadId" or "excludeTurns"))) Environment.Exit(10);
                    resumes++; result = new { thread = Thread(mode == "uuid-change" ? Guid.NewGuid().ToString() : null) }; break;
                case "thread/turns/list":
                    bool moreTurns = mode == "pagination" && (!p.TryGetProperty("cursor", out var tc) || tc.ValueKind == JsonValueKind.Null);
                    object turn = mode == "pagination" && !moreTurns ? new { id = "second-turn", status = "completed" } : Turn();
                    result = new { data = new[] { turn }, nextCursor = moreTurns ? "turn-page-2" : null }; break;
                case "thread/items/list":
                    bool moreItems = mode == "pagination" && p.GetProperty("cursor").ValueKind == JsonValueKind.Null;
                    bool secondItem = mode == "pagination" && !moreItems;
                    object item = new { type = "userMessage", id = secondItem ? "item-2" : "item-1",
                        content = new[] { new { type = "text", text = mode == "context-change" && resumes > 0 ? "Changed task" : "Original task" } } };
                    result = new { data = mode == "empty-context" ? Array.Empty<object>() : new[] { (object)new { turnId = secondItem ? "second-turn" : turnId, item } },
                        nextCursor = moreItems ? "item-page-2" : null }; break;
                case "account/rateLimits/read": result = new { ordinaryUsageAllowed = true, rateLimits = new { primary = new { usedPercent = 12 } } }; break;
                case "turn/start":
                    if (p.GetProperty("input")[0].GetProperty("text").GetString() != "fixture prompt" ||
                        p.GetProperty("clientUserMessageId").GetString() != "attempt-fixture") Environment.Exit(9);
                    turnId = "new-turn"; turnStatus = "inProgress"; result = new { turn = Turn() }; break;
                default: Send(new { id, error = new { code = -32601, message = "Unsupported" } }); continue;
            }
            Send(new { id, result });
            if (method.GetString() == "turn/start")
                Send(new { id = "approval-1", method = "item/commandExecution/requestApproval", @params = new { threadId = Id, turnId } });
        }
    }
}
