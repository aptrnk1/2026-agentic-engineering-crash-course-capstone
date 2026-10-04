namespace AgentLog.Core;

/// <summary>Per-tool counters, same meaning as day01 scripts/agent-log-summary.mjs.</summary>
/// <param name="Blocked">Pre lines without a Post, after which the session executed something else.</param>
/// <param name="Pending">Pre lines without a Post and nothing executed after them in the session:
/// the call may still be running (e.g. agentlog itself), so it is not counted as blocked.</param>
public sealed record ToolStats(
    string Tool,
    int Proposed,
    int Executed,
    int Blocked,
    int Failed,
    long TotalMs,
    int UniqueFiles,
    int Pending = 0);

/// <param name="Tools">Sorted by Proposed + Executed, descending; ties keep first-seen order.</param>
/// <param name="SkippedLines">Non-blank lines that could not be parsed.</param>
/// <param name="Sessions">Distinct session ids among the matching lines.</param>
/// <param name="First">Earliest timestamp among the matching lines.</param>
/// <param name="Last">Latest timestamp among the matching lines.</param>
public sealed record Summary(
    IReadOnlyList<ToolStats> Tools,
    int SkippedLines,
    int Sessions = 0,
    DateTimeOffset? First = null,
    DateTimeOffset? Last = null)
{
    public int Executed => Tools.Sum(t => t.Executed);
    public int Blocked => Tools.Sum(t => t.Blocked);
    public int Pending => Tools.Sum(t => t.Pending);
    public int Failed => Tools.Sum(t => t.Failed);
}

/// <summary>A file path seen in executed actions.</summary>
/// <param name="Actions">Executed actions (Post lines) on this path.</param>
/// <param name="Tools">Distinct tools that touched it, in first-seen order.</param>
public sealed record FileTouch(string Path, int Actions, IReadOnlyList<string> Tools);

public static class Aggregator
{
    private const string PreToolUse = "PreToolUse";
    private static readonly ExitStatus Success = new(ExitKind.Code, 0);

    public static Summary Summarize(IEnumerable<string> lines, LogFilter? filter = null)
    {
        var (entries, skipped) = Load(lines, filter);
        var unmatched = Unmatched(entries);

        var byTool = new Dictionary<string, Counter>();
        var order = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (!byTool.TryGetValue(e.Tool, out var c))
            {
                byTool[e.Tool] = c = new Counter();
                order.Add(e.Tool);
            }

            if (e.Event == PreToolUse)
            {
                c.Proposed++;
                if (unmatched.TryGetValue(i, out var pending))
                {
                    if (pending)
                        c.Pending++;
                    else
                        c.Blocked++;
                }
            }
            else
            {
                c.Executed++;
                c.Ms += e.Ms ?? 0;
                if (IsFailed(e))
                    c.Failed++;
            }

            if (e.Path is not null)
                c.Files.Add(e.Path);
        }

        var tools = order
            .Select(t => (Tool: t, C: byTool[t]))
            .OrderByDescending(x => x.C.Proposed + x.C.Executed) // stable: ties keep first-seen order
            .Select(x => new ToolStats(x.Tool, x.C.Proposed, x.C.Executed, x.C.Blocked, x.C.Failed, x.C.Ms, x.C.Files.Count, x.C.Pending))
            .ToList();

        return new Summary(
            tools,
            skipped,
            entries.Select(e => e.Session).Where(s => s.Length > 0).Distinct().Count(),
            entries.Count > 0 ? entries.Min(e => e.Ts) : null,
            entries.Count > 0 ? entries.Max(e => e.Ts) : null);
    }

    /// <summary>Pre lines whose id never got a Post line, in log order. Pending lines are not included.</summary>
    public static IReadOnlyList<LogEntry> Blocked(IEnumerable<string> lines, LogFilter? filter = null)
    {
        var (entries, _) = Load(lines, filter);
        var unmatched = Unmatched(entries);
        return entries.Where((_, i) => unmatched.TryGetValue(i, out var pending) && !pending).ToList();
    }

    /// <summary>Executed lines with a non-zero exit, in log order. Lines without `exit` are not failed.</summary>
    public static IReadOnlyList<LogEntry> Failed(IEnumerable<string> lines, LogFilter? filter = null)
    {
        var (entries, _) = Load(lines, filter);
        return entries.Where(e => e.Event != PreToolUse && IsFailed(e)).ToList();
    }

    /// <summary>Paths of executed actions, sorted by path.</summary>
    public static IReadOnlyList<FileTouch> Files(IEnumerable<string> lines, LogFilter? filter = null)
    {
        var (entries, _) = Load(lines, filter);
        return entries
            .Where(e => e.Event != PreToolUse && e.Path is not null)
            .GroupBy(e => e.Path!, StringComparer.Ordinal)
            .Select(g => new FileTouch(g.Key, g.Count(), g.Select(e => e.Tool).Distinct().ToList()))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
    }

    private static (List<LogEntry> Entries, int Skipped) Load(IEnumerable<string> lines, LogFilter? filter)
    {
        var entries = new List<LogEntry>();
        var skipped = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var result = LogLineParser.Parse(line);
            if (result.Entry is not { } entry)
                skipped++;
            else if (filter is null || filter.Matches(entry))
                entries.Add(entry);
        }
        return (entries, skipped);
    }

    // Every non-Pre event (PostToolUse, PostToolUseFailure) means the call actually ran.
    private static HashSet<string> ExecutedIds(IEnumerable<LogEntry> entries) =>
        entries.Where(e => e.Event != PreToolUse && e.Id is not null).Select(e => e.Id!).ToHashSet();

    /// <summary>
    /// Indexes of Pre lines that never got a Post line of the same id, mapped to "pending":
    /// true when nothing executed after them in the same session (the call may still be running).
    /// </summary>
    private static Dictionary<int, bool> Unmatched(List<LogEntry> entries)
    {
        var executedIds = ExecutedIds(entries);
        var lastExecuted = new Dictionary<string, int>();
        for (var i = 0; i < entries.Count; i++)
            if (entries[i].Event != PreToolUse)
                lastExecuted[entries[i].Session] = i;

        var unmatched = new Dictionary<int, bool>();
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.Event == PreToolUse && e.Id is not null && !executedIds.Contains(e.Id))
                unmatched[i] = !lastExecuted.TryGetValue(e.Session, out var last) || last < i;
        }
        return unmatched;
    }

    // No `exit` at all is unknown, not a failure.
    private static bool IsFailed(LogEntry executed) => executed.Exit is { } exit && exit != Success;

    private sealed class Counter
    {
        public int Proposed, Executed, Blocked, Pending, Failed;
        public long Ms;
        public HashSet<string> Files { get; } = [];
    }
}
