using System.ComponentModel;

namespace CodexAutoResume.Core;

public sealed class ConversationView : INotifyPropertyChanged
{
    private Conversation conversation;
    private int priority, queuePosition;
    public ConversationView(Conversation conversation) { this.conversation = conversation; }
    public string Id => conversation.Id;
    public string Heading => $"{priority}. {conversation.Name}";
    public string Workspace => conversation.Workspace;
    public string Reason => conversation.ObservationOnly && conversation.Attention ? "Read-only; Desktop activity unknown. " + conversation.Reason : conversation.ObservationOnly || conversation.Revalidated ? conversation.Reason : "Saved state; awaiting Codex revalidation. " + conversation.Reason;
    public bool IsRunning => conversation.ObservationOnly ? conversation.ObservedActive : conversation.Status == ConversationStatus.Running && conversation.Revalidated;
    public string StatusLabel => conversation.ObservationOnly ? (conversation.ObservedActive ? "Running · connected server" : "Activity unknown") : (conversation.Status switch
    {
        ConversationStatus.RateLimited => "Rate Limited",
        ConversationStatus.NeedsAttention => "Needs Attention",
        _ => conversation.Status.ToString()
    }) + (conversation.Revalidated ? "" : " · unverified");
    public string BadgeBrush => conversation.ObservationOnly && conversation.ObservedActive ? "#216CBA" : !conversation.Revalidated ? "#4C5C59" : conversation.Status switch
    {
        ConversationStatus.Running => "#216CBA",
        ConversationStatus.Queued => "#6954A6",
        ConversationStatus.RateLimited => "#8D5B17",
        ConversationStatus.Completed => "#24704B",
        ConversationStatus.NeedsAttention => "#AC404F",
        _ => "#556174"
    };
    public string ParallelLabel => conversation.Parallel ? "★ Parallel" : "☆ Sequential";
    public string PauseLabel => conversation.Paused ? "Unpause" : "Pause";
    public string Timing
    {
        get
        {
            var now = DateTimeOffset.UtcNow;
            if (conversation.ObservationOnly) return (conversation.LastUsedAt is { } used ? $"Backend activity {used.ToLocalTime():g}" : "Recent-use timestamp unavailable") +
                (conversation.ObservedTurnStatus == "completed" ? " · last recorded work completed" : "") + " · execution OFF";
            if (!conversation.Revalidated) return "Connect to verify the saved status before scheduling";
            if (IsRunning) return conversation.StartedAt is { } start ? $"Running for {Duration(now - start)}" : "Running · start time unavailable";
            if (conversation.Status == ConversationStatus.RateLimited)
            {
                var check = conversation.NextCheckAt is { } next ? $" · next verification {next.ToLocalTime():g}" : "";
                return conversation.ResetAt is { } reset
                    ? $"Estimated reset {reset.ToLocalTime():g} · {Duration(reset - now)} remaining{check}"
                    : "Reset unknown · conservative backoff" + check;
            }
            if (conversation.Status == ConversationStatus.Queued) return $"Queue position {queuePosition} · {(conversation.Parallel ? "parallel permitted" : "sequential lane")}";
            if (conversation.Status == ConversationStatus.Completed) return conversation.CompletedAt is { } done
                ? $"{(conversation.CompletionTimeEstimated ? "Completion observed" : "Completed")} {done.ToLocalTime():g}" : "Completed";
            return conversation.ExecutionActive ? "Execution is still active; future scheduling is blocked" : conversation.Armed ? "Armed for continuation" : "Disarmed";
        }
    }
    private static string Duration(TimeSpan span) => $"{(int)Math.Max(0, span.TotalHours):00}:{Math.Max(0, span.Minutes):00}:{Math.Max(0, span.Seconds):00}";
    public void Update(Conversation next, int priority, int queuePosition)
    {
        var wasRunning = IsRunning;
        conversation = next; this.priority = priority; this.queuePosition = queuePosition;
        foreach (var property in new[] { nameof(Heading), nameof(Workspace), nameof(Reason), nameof(StatusLabel), nameof(BadgeBrush), nameof(ParallelLabel), nameof(PauseLabel), nameof(Timing) })
            PropertyChanged?.Invoke(this, new(property));
        if (wasRunning != IsRunning) PropertyChanged?.Invoke(this, new(nameof(IsRunning)));
    }
    public void RefreshClock() => PropertyChanged?.Invoke(this, new(nameof(Timing)));
    public event PropertyChangedEventHandler? PropertyChanged;
}
