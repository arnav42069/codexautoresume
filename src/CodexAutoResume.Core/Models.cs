using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexAutoResume.Core;

public enum ConversationStatus { Running, Queued, RateLimited, Completed, NeedsAttention, Paused }
public enum AttemptPhase { Prepared, Accepted, Completed, RateLimited, Failed, ResolvedManually }

public sealed class Conversation
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Workspace { get; set; } = "";
    public string RepositoryKey { get; set; } = "";
    public bool Parallel { get; set; }
    public bool Armed { get; set; }
    public bool Paused { get; set; }
    public bool Attention { get; set; }
    public ConversationStatus Status { get; set; } = ConversationStatus.Paused;
    public string Reason { get; set; } = "Revalidation required";
    public string Prompt { get; set; } = DefaultPrompt;
    public string? LastTurnId { get; set; }
    public string? ArmedAfterTurnId { get; set; }
    public string? AttemptId { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool CompletionTimeEstimated { get; set; }
    public DateTimeOffset? ResetAt { get; set; }
    public DateTimeOffset? NextCheckAt { get; set; }
    public bool WaitingForUsage { get; set; }
    public int BackoffMinutes { get; set; } = 15;
    public bool Revalidated { get; set; }
    public bool ExecutionActive { get; set; }
    public bool ObservationOnly { get; set; }
    public bool ObservedActive { get; set; }
    public string? ObservedTurnStatus { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public const string DefaultPrompt = "Continue the previously interrupted task from where you stopped. Inspect the current work and conversation context, avoid repeating completed steps, and finish the remaining tasks. Do not start unrelated work.";
}

public sealed class ResumeAttempt
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ThreadId { get; set; } = "";
    public string? BaseTurnId { get; set; }
    public string? TurnId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public AttemptPhase Phase { get; set; }
}

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    public bool Enabled { get; set; }
    public string CodexPath { get; set; } = "codex.exe";
    public bool StartWithWindows { get; set; }
    public AppearanceSettings Appearance { get; set; } = new();
    public DateTimeOffset? LastDiscoveryAt { get; set; }
    public string DiscoveryOrder { get; set; } = "Recent chats have not been checked";
    public string? DiscoveryError { get; set; }
    public List<Conversation> Conversations { get; set; } = [];
    public List<ResumeAttempt> Attempts { get; set; } = [];
    public List<string> Log { get; set; } = [];
}

public record TurnSnapshot(string Id, string Status, string? ErrorCode = null, string? ErrorMessage = null,
    DateTimeOffset? StartedAt = null, DateTimeOffset? CompletedAt = null);
public record ThreadSnapshot(string Id, string Name, string Workspace, string RuntimeStatus,
    string[] ActiveFlags, TurnSnapshot? LastTurn, DateTimeOffset? RecencyAt = null,
    DateTimeOffset? UpdatedAt = null, DateTimeOffset? CreatedAt = null)
{
    public bool VerifiedActive => RuntimeStatus == "active";
    public DateTimeOffset? RecentActivityAt => RecencyAt ?? UpdatedAt ?? CreatedAt;
}
public record RecentThreadListing(IReadOnlyList<ThreadSnapshot> Threads, string OrderDescription);
public record UsageSnapshot(bool? OrdinaryUsageAllowed, bool Exhausted, DateTimeOffset? ResetAt, string Detail)
{
    public bool CanResume => OrdinaryUsageAllowed == true && !Exhausted;
}
public record CodexEvent(string Method, JsonElement Parameters, JsonElement? RequestId = null);

public interface ICodexClient : IAsyncDisposable
{
    event Action<CodexEvent>? EventReceived;
    event Action<string>? Disconnected;
    Task ConnectAsync(string executable, CancellationToken ct);
    Task<IReadOnlyList<ThreadSnapshot>> ListAsync(CancellationToken ct);
    Task<RecentThreadListing> ListRecentAsync(CancellationToken ct);
    Task<ThreadSnapshot> ReadAsync(string id, CancellationToken ct);
    Task<ThreadSnapshot> ResumeAsync(string id, CancellationToken ct);
    Task<TurnSnapshot> StartTurnAsync(string id, string prompt, string attemptId, CancellationToken ct);
    Task<UsageSnapshot> ReadUsageAsync(CancellationToken ct);
    Task RejectRequestAsync(CodexEvent request, CancellationToken ct);
}

public static class StateJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public static AppState Clone(AppState state) => JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state, Options), Options)!;
}
