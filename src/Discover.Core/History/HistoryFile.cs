namespace Discover.Core.History;

/// <summary>One stored observation: a time and a count (live users, or visits).</summary>
public sealed class HistoryRow
{
    public DateTime Utc { get; set; }
    public int Value { get; set; }
}

/// <summary>history.json (spec "Local history"): world keys, counts and timestamps only.</summary>
public sealed class HistoryFile
{
    public int Version { get; set; } = 1;
    public Dictionary<string, List<HistoryRow>> Live { get; set; } = [];
    public Dictionary<string, List<HistoryRow>> Visits { get; set; } = [];
}
