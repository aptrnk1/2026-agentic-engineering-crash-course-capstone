namespace AgentLog.Core.Tests;

public class LogLineParserTests
{
    [Fact]
    public void Parses_PreToolUse_line()
    {
        const string line = """{"ts":"2026-10-04T10:27:46.548Z","event":"PreToolUse","id":"toolu_01","session":"1613274a","mode":"default","tool":"Read","path":"AGENTS.md"}""";

        var result = LogLineParser.Parse(line);

        Assert.False(result.Skipped);
        Assert.Equal(
            new LogEntry(
                Ts: new DateTimeOffset(2026, 10, 4, 10, 27, 46, 548, TimeSpan.Zero),
                Event: "PreToolUse",
                Id: "toolu_01",
                Session: "1613274a",
                Mode: "default",
                Tool: "Read",
                Path: "AGENTS.md"),
            result.Entry);
    }

    [Fact]
    public void Parses_PostToolUse_with_numeric_exit_and_ms()
    {
        const string line = """{"ts":"2026-10-04T10:31:19.200Z","event":"PostToolUse","id":"toolu_02","session":"1613274a","mode":"default","tool":"Bash","cmd":"dotnet test","exit":0,"ms":11409}""";

        var entry = LogLineParser.Parse(line).Entry;

        Assert.NotNull(entry);
        Assert.Equal("PostToolUse", entry.Event);
        Assert.Equal("dotnet test", entry.Cmd);
        Assert.Equal(new ExitStatus(ExitKind.Code, 0), entry.Exit);
        Assert.Equal(11409, entry.Ms);
    }

    [Fact]
    public void Parses_interrupted_exit()
    {
        const string line = """{"ts":"2026-10-04T10:40:00.000Z","event":"PostToolUseFailure","id":"toolu_03","session":"1613274a","mode":"default","tool":"Bash","cmd":"sleep 100","exit":"interrupted","ms":5000}""";

        var entry = LogLineParser.Parse(line).Entry;

        Assert.NotNull(entry);
        Assert.Equal(new ExitStatus(ExitKind.Interrupted), entry.Exit);
    }

    [Fact]
    public void Unknown_exit_string_is_read_as_error_not_skipped()
    {
        // Review 001 #7: one odd field must not drop the whole line.
        const string line = """{"ts":"2026-10-04T10:40:00.000Z","event":"PostToolUseFailure","id":"toolu_04","session":"1613274a","mode":"default","tool":"Bash","cmd":"x","exit":"timeout","ms":5}""";

        var entry = LogLineParser.Parse(line).Entry;

        Assert.NotNull(entry);
        Assert.Equal(new ExitStatus(ExitKind.Error), entry.Exit);
        Assert.Equal(5, entry.Ms);
    }

    [Fact]
    public void Exit_code_above_int_range_is_read_as_error_not_skipped()
    {
        // Review 002 #1: Windows crash codes like 0xC0000005 do not fit in an int.
        const string line = """{"ts":"2026-10-04T10:40:00.000Z","event":"PostToolUseFailure","id":"toolu_06","session":"1613274a","mode":"default","tool":"Bash","cmd":"x","exit":3221225477,"ms":5}""";

        var result = LogLineParser.Parse(line);

        Assert.False(result.Skipped);
        Assert.Equal(new ExitStatus(ExitKind.Error), result.Entry!.Exit);
    }

    [Theory]
    [InlineData("12.5", 13)]
    [InlineData("12.4", 12)]
    [InlineData("7", 7)]
    public void Non_integer_ms_is_rounded_not_skipped(string ms, int expected)
    {
        var line = $$"""{"ts":"2026-10-04T10:40:00.000Z","event":"PostToolUse","id":"toolu_05","session":"1613274a","mode":"default","tool":"Bash","cmd":"x","exit":0,"ms":{{ms}}}""";

        var entry = LogLineParser.Parse(line).Entry;

        Assert.NotNull(entry);
        Assert.Equal(expected, entry.Ms);
    }

    [Theory]
    [InlineData("""{"ts":"2026-10-04T10:27:46.548Z","event":""")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void Broken_json_is_skipped_not_thrown(string line)
    {
        var result = LogLineParser.Parse(line);

        Assert.True(result.Skipped);
        Assert.Null(result.Entry);
        Assert.False(string.IsNullOrWhiteSpace(result.SkipReason));
    }

    [Fact]
    public void Line_without_id_is_parsed_with_null_id()
    {
        // The hook omits `id` when tool_use_id is missing; the line is still a valid action.
        const string line = """{"ts":"2026-10-04T10:27:46.548Z","event":"PreToolUse","session":"1613274a","mode":"default","tool":"Read","path":"AGENTS.md"}""";

        var result = LogLineParser.Parse(line);

        Assert.False(result.Skipped);
        Assert.NotNull(result.Entry);
        Assert.Null(result.Entry.Id);
        Assert.Equal("Read", result.Entry.Tool);
    }
}
