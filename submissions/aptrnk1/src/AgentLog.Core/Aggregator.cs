namespace AgentLog.Core;

/// <summary>Per-tool counters, same meaning as day01 scripts/agent-log-summary.mjs.</summary>
public sealed record ToolStats(
    string Tool,
    int Proposed,
    int Executed,
    int Blocked,
    int Failed,
    long TotalMs,
    int UniqueFiles);

/// <param name="Tools">Sorted by Proposed + Executed, descending; ties keep first-seen order.</param>
/// <param name="SkippedLines">Non-blank lines that could not be parsed.</param>
public sealed record Summary(IReadOnlyList<ToolStats> Tools, int SkippedLines);

public static class Aggregator
{
    public static Summary Summarize(IEnumerable<string> lines) => throw new NotImplementedException();
}
