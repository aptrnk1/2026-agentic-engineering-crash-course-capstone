namespace AgentLog.Core.Tests;

public class AggregatorListsTests
{
    private static IEnumerable<string> Basic() =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "basic.jsonl"));

    private static string Line(string session, string ts, string evt, string id) =>
        $$"""{"ts":"{{ts}}","event":"{{evt}}","id":"{{id}}","session":"{{session}}","mode":"auto","tool":"Bash","cmd":"x"}""";

    [Fact]
    public void Blocked_lists_pre_lines_without_post()
    {
        var blocked = Assert.Single(Aggregator.Blocked(Basic()));

        Assert.Equal("toolu_01DCDKx9uGha51vsP73HkKqb", blocked.Id);
        Assert.Equal("cat -n .agent-log/actions.jsonl | cut -c1-260", blocked.Cmd);
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
        Assert.Equal(new ToolStats("Bash", 1, 0, 1, 0, 0, 0), Assert.Single(summary.Tools));
    }

    [Fact]
    public void Since_filter_keeps_lines_at_or_after_the_moment()
    {
        var since = new DateTimeOffset(2026, 10, 4, 11, 0, 0, TimeSpan.Zero);

        var summary = Aggregator.Summarize(Basic(), new LogFilter(Since: since));

        // Only the last fixture line (11:20:40, Bash Pre without Post) is after 11:00.
        Assert.Equal(new ToolStats("Bash", 1, 0, 1, 0, 0, 0), Assert.Single(summary.Tools));
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T11:20:40.969Z"), summary.First);
    }

    [Fact]
    public void Summary_reports_sessions_and_time_range_of_fixture()
    {
        var summary = Aggregator.Summarize(Basic());

        Assert.Equal((9, 1, 1, 1), (summary.Executed, summary.Blocked, summary.Failed, summary.Sessions));
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
