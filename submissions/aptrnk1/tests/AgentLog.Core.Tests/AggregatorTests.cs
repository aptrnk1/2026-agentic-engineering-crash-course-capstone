namespace AgentLog.Core.Tests;

public class AggregatorTests
{
    private static IEnumerable<string> Fixture(string name) =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    private static string Line(string evt, string id, string tool, string extra = "") =>
        $$"""{"ts":"2026-10-04T10:00:00.000Z","event":"{{evt}}"{{(id == "" ? "" : $",\"id\":\"{id}\"")}},"session":"s1","mode":"auto","tool":"{{tool}}"{{extra}}}""";

    [Fact]
    public void Summarizes_basic_fixture_per_tool()
    {
        var summary = Aggregator.Summarize(Fixture("basic.jsonl"));

        Assert.Equal(
            new[]
            {
                // 4 Pre + 4 Post; LogLineParser.cs written twice -> 3 unique files
                new ToolStats("Write", Proposed: 4, Executed: 4, Blocked: 0, Failed: 0, TotalMs: 33 + 20 + 16 + 50, UniqueFiles: 3),
                // last Pre has no Post -> blocked; PostToolUseFailure exit 1 -> failed
                new ToolStats("Bash", Proposed: 4, Executed: 3, Blocked: 1, Failed: 1, TotalMs: 820 + 11409 + 173, UniqueFiles: 0),
                new ToolStats("Read", Proposed: 1, Executed: 1, Blocked: 0, Failed: 0, TotalMs: 13, UniqueFiles: 1),
                new ToolStats("Agent", Proposed: 1, Executed: 1, Blocked: 0, Failed: 0, TotalMs: 11, UniqueFiles: 0),
            },
            summary.Tools);
        Assert.Equal(0, summary.SkippedLines);
    }

    [Fact]
    public void Pre_without_id_is_proposed_but_not_blocked()
    {
        var summary = Aggregator.Summarize([Line("PreToolUse", "", "Bash")]);

        var bash = Assert.Single(summary.Tools);
        Assert.Equal(1, bash.Proposed);
        Assert.Equal(0, bash.Blocked);
    }

    [Theory]
    [InlineData("\"interrupted\"")]
    [InlineData("\"error\"")]
    [InlineData("2")]
    public void Non_zero_exit_counts_as_failed(string exit)
    {
        var summary = Aggregator.Summarize(
        [
            Line("PreToolUse", "t1", "Bash"),
            Line("PostToolUseFailure", "t1", "Bash", $",\"exit\":{exit},\"ms\":5"),
        ]);

        var bash = Assert.Single(summary.Tools);
        Assert.Equal(new ToolStats("Bash", 1, 1, 0, 1, 5, 0), bash);
    }

    [Fact]
    public void Executed_line_without_exit_is_not_failed()
    {
        // Review 001 #3: a Post line with no `exit` (older hook, trimmed log) is not evidence of failure.
        string[] lines =
        [
            Line("PreToolUse", "t1", "Bash"),
            Line("PostToolUse", "t1", "Bash", ",\"ms\":5"),
        ];

        Assert.Equal(new ToolStats("Bash", 1, 1, 0, 0, 5, 0), Assert.Single(Aggregator.Summarize(lines).Tools));
        Assert.Empty(Aggregator.Failed(lines));
    }

    [Fact]
    public void Broken_lines_are_counted_as_skipped_and_blank_lines_ignored()
    {
        var summary = Aggregator.Summarize(
        [
            Line("PreToolUse", "t1", "Read", ",\"path\":\"a.txt\""),
            "",
            "   ",
            "{not json",
            Line("PostToolUse", "t1", "Read", ",\"path\":\"a.txt\",\"exit\":0,\"ms\":7"),
        ]);

        Assert.Equal(1, summary.SkippedLines);
        Assert.Equal(new ToolStats("Read", 1, 1, 0, 0, 7, 1), Assert.Single(summary.Tools));
    }
}
