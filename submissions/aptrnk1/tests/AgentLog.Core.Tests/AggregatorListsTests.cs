namespace AgentLog.Core.Tests;

public class AggregatorListsTests
{
    private static IEnumerable<string> Basic() =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "basic.jsonl"));

    private static string Line(string session, string ts, string evt, string id) =>
        $$"""{"ts":"{{ts}}","event":"{{evt}}","id":"{{id}}","session":"{{session}}","mode":"auto","tool":"Bash","cmd":"x"}""";

    [Fact]
    public void Blocked_lists_pre_lines_without_post_when_the_session_moved_on()
    {
        string[] lines =
        [
            Line("aaaaaaaa", "2026-10-04T10:00:00Z", "PreToolUse", "denied"),
            Line("aaaaaaaa", "2026-10-04T10:00:01Z", "PreToolUse", "t2"),
            Line("aaaaaaaa", "2026-10-04T10:00:02Z", "PostToolUse", "t2"),
        ];

        Assert.Equal("denied", Assert.Single(Aggregator.Blocked(lines)).Id);
        Assert.Equal(new ToolStats("Bash", 2, 1, 1, 0, 0, 0), Assert.Single(Aggregator.Summarize(lines).Tools));
    }

    [Fact]
    public void Pre_without_post_and_nothing_executed_after_is_pending_not_blocked()
    {
        // Review 001 #5: the fixture's last line is `agentlog` itself still running, not a block.
        Assert.Empty(Aggregator.Blocked(Basic()));

        var summary = Aggregator.Summarize(Basic());
        Assert.Equal((0, 1), (summary.Blocked, summary.Pending));
        Assert.Equal(1, summary.Tools.Single(t => t.Tool == "Bash").Pending);
    }

    [Fact]
    public void Pending_is_decided_per_session()
    {
        string[] lines =
        [
            Line("aaaaaaaa", "2026-10-04T10:00:00Z", "PreToolUse", "t1"),
            Line("bbbbbbbb", "2026-10-04T10:00:01Z", "PreToolUse", "t2"),
            Line("bbbbbbbb", "2026-10-04T10:00:02Z", "PostToolUse", "t2"),
        ];

        var summary = Aggregator.Summarize(lines);

        Assert.Equal((0, 1), (summary.Blocked, summary.Pending));
    }

    [Fact]
    public void Failed_lists_executed_lines_with_non_zero_exit()
    {
        var failed = Assert.Single(Aggregator.Failed(Basic()));

        Assert.Equal("PostToolUseFailure", failed.Event);
        Assert.Equal(new ExitStatus(ExitKind.Code, 1), failed.Exit);
    }

    [Fact]
    public void Files_lists_paths_of_executed_actions_sorted()
    {
        var files = Aggregator.Files(Basic());

        Assert.Equal(
            ["AGENTS.md", "src/AgentLog.Core/LogEntry.cs", "src/AgentLog.Core/LogLineParser.cs",
             "tests/AgentLog.Core.Tests/LogLineParserTests.cs"],
            files.Select(f => f.Path));
        var parser = files.Single(f => f.Path == "src/AgentLog.Core/LogLineParser.cs");
        Assert.Equal(2, parser.Actions);
        Assert.Equal(["Write"], parser.Tools);
    }

    [Fact]
    public void Session_filter_keeps_only_that_session()
    {
        string[] lines =
        [
            Line("aaaaaaaa", "2026-10-04T10:00:00Z", "PreToolUse", "t1"),
            Line("aaaaaaaa", "2026-10-04T10:00:01Z", "PostToolUse", "t1"),
            Line("bbbbbbbb", "2026-10-04T10:00:02Z", "PreToolUse", "t2"),
        ];

        var summary = Aggregator.Summarize(lines, new LogFilter(Session: "bbbbbbbb"));

        Assert.Equal(1, summary.Sessions);
        Assert.Equal(new ToolStats("Bash", 1, 0, 0, 0, 0, 0, Pending: 1), Assert.Single(summary.Tools));
    }

    [Fact]
    public void Since_filter_keeps_lines_at_or_after_the_moment()
    {
        var since = new DateTimeOffset(2026, 10, 4, 11, 0, 0, TimeSpan.Zero);

        var summary = Aggregator.Summarize(Basic(), new LogFilter(Since: since));

        // Only the last fixture line (11:20:40, Bash Pre without Post) is after 11:00.
        Assert.Equal(new ToolStats("Bash", 1, 0, 0, 0, 0, 0, Pending: 1), Assert.Single(summary.Tools));
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T11:20:40.969Z"), summary.First);
    }

    [Fact]
    public void Summary_reports_sessions_and_time_range_of_fixture()
    {
        var summary = Aggregator.Summarize(Basic());

        Assert.Equal((9, 0, 1, 1, 1), (summary.Executed, summary.Blocked, summary.Pending, summary.Failed, summary.Sessions));
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T10:27:46.548Z"), summary.First);
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T11:20:40.969Z"), summary.Last);
    }

    [Fact]
    public void Markdown_escapes_pipes_and_newlines_in_targets()
    {
        var entry = new LogEntry(DateTimeOffset.UnixEpoch, "PreToolUse", "t1", "s", "auto", "Bash", Cmd: "a | b\nc");

        var md = ReportFormatter.Blocked([entry], OutputFormat.Md);

        Assert.Contains("| a \\| b\\nc |", md);
    }
}
