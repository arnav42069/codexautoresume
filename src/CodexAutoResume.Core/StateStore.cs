using System.Text.Json;

namespace CodexAutoResume.Core;

public interface IStateStore
{
    AppState Load();
    void Save(AppState state);
}

// One atomic transaction persists queue and journal together before any turn/start.
public sealed class JsonStateStore(string path) : IStateStore
{
    public AppState Load()
    {
        if (!File.Exists(path)) return new AppState();
        var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), StateJson.Options)
            ?? throw new InvalidDataException("Empty state file. Restore a known-good backup; scheduling is disabled.");
        if (state.SchemaVersion != 1 || state.Conversations.Count > 5 ||
            state.Conversations.Any(c => !Guid.TryParse(c.Id, out _)) ||
            state.Conversations.Select(c => Guid.Parse(c.Id)).Distinct().Count() != state.Conversations.Count ||
            state.Conversations.Any(c => !Enum.IsDefined(c.Status) || string.IsNullOrWhiteSpace(c.Prompt) ||
                string.IsNullOrWhiteSpace(c.Workspace) || (c.AttemptId != null &&
                    !state.Attempts.Any(a => a.Id == c.AttemptId && a.ThreadId == c.Id))) ||
            state.Attempts.Any(a => !Enum.IsDefined(a.Phase) ||
                ((a.Phase is AttemptPhase.Prepared or AttemptPhase.Accepted) &&
                 !state.Conversations.Any(c => c.Id == a.ThreadId && c.AttemptId == a.Id))) ||
            state.Attempts.Select(a => a.Id).Distinct().Count() != state.Attempts.Count)
            throw new InvalidDataException("Invalid or unsupported state file. Scheduling is disabled.");
        if (state.Appearance == null || !ThemePalette.TryNormalize(state.Appearance.Accent, out _))
            state.Appearance = new AppearanceSettings();
        return state;
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, state, StateJson.Options);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
        else File.Move(temp, path);
    }
}
