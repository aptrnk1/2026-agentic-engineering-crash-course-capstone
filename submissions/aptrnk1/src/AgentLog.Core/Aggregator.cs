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
    private const string PreToolUse = "PreToolUse";
    private static readonly ExitStatus Success = new(ExitKind.Code, 0);

    public static Summary Summarize(IEnumerable<string> lines)
    {
        var entries = new List<LogEntry>();
        var skipped = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var result = LogLineParser.Parse(line);
            if (result.Entry is { } entry)
                entries.Add(entry);
            else
                skipped++;
        }

        // Every non-Pre event (PostToolUse, PostToolUseFailure) means the call actually ran.
        var executedIds = entries
            .Where(e => e.Event != PreToolUse && e.Id is not null)
            .Select(e => e.Id!)
            .ToHashSet();

        var byTool = new Dictionary<string, Counter>();
        var order = new List<string>();
        foreach (var e in entries)
        {
            if (!byTool.TryGetValue(e.Tool, out var c))
            {
                byTool[e.Tool] = c = new Counter();
                order.Add(e.Tool);
            }

            if (e.Event == PreToolUse)
            {
                c.Proposed++;
                if (e.Id is not null && !executedIds.Contains(e.Id))
                    c.Blocked++;
            }
            else
            {
                c.Executed++;
                c.Ms += e.Ms ?? 0;
                if (e.Exit != Success)
                    c.Failed++;
            }

            if (e.Path is not null)
                c.Files.Add(e.Path);
        }

        var tools = order
            .Select(t => (Tool: t, C: byTool[t]))
            .OrderByDescending(x => x.C.Proposed + x.C.Executed) // stable: ties keep first-seen order
            .Select(x => new ToolStats(x.Tool, x.C.Proposed, x.C.Executed, x.C.Blocked, x.C.Failed, x.C.Ms, x.C.Files.Count))
            .ToList();

        return new Summary(tools, skipped);
    }

    private sealed class Counter
    {
        public int Proposed, Executed, Blocked, Failed;
        public long Ms;
        public HashSet<string> Files { get; } = [];
    }
}
