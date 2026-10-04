using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AgentLog.Cli.Tests;

/// <summary>Runs the real agentlog executable as a child process.</summary>
public class CliEndToEndTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "basic.jsonl");

    private sealed record Run(int ExitCode, string Stdout, string Stderr)
    {
        public string[] Lines => Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        /// <summary>Whitespace-separated cells of the first line that starts with <paramref name="first"/>.</summary>
        public string[] Row(string first) =>
            Lines.First(l => l.StartsWith(first + " ", StringComparison.Ordinal))
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static Run AgentlogIn(string? workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "agentlog.dll"));
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return new Run(p.ExitCode, stdout.Replace("\r\n", "\n"), stderr.Result);
    }

    private static Run Agentlog(params string[] args) => AgentlogIn(null, args);

    private static string TempLog(params string[] lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void Summary_is_the_default_command()
    {
        var run = Agentlog(Fixture);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Stderr);
        Assert.Equal(
            "Agent actions: 9 executed, 0 blocked, 1 pending, 1 failed; 1 session(s), 2026-10-04T10:27:46.548Z .. 2026-10-04T11:20:40.969Z",
            run.Lines[0]);
        Assert.Equal(
            [
                "tool   proposed  executed  blocked  failed  time (s)  files",
                "Write         4         4        0       0       0.1      3",
                "Bash          4         3        0       1      12.4      0",
                "Read          1         1        0       0       0.0      1",
                "Agent         1         1        0       0       0.0      0",
            ],
            run.Lines[1..]);
    }

    [Fact]
    public void Summary_command_matches_default()
    {
        Assert.Equal(Agentlog(Fixture).Stdout, Agentlog("summary", Fixture).Stdout);
    }

    [Fact]
    public void Summary_as_markdown()
    {
        var run = Agentlog("summary", Fixture, "--format", "md");

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith("**Agent actions: 9 executed, 0 blocked, 1 pending, 1 failed;", run.Stdout);
        Assert.Contains("| tool | proposed | executed | blocked | failed | time (s) | files |\n|---|---:|---:|---:|---:|---:|---:|\n", run.Stdout);
        Assert.Contains("| Bash | 4 | 3 | 0 | 1 | 12.4 | 0 |\n", run.Stdout);
    }

    [Fact]
    public void Summary_as_json()
    {
        var run = Agentlog("summary", Fixture, "--format", "json");

        Assert.Equal(0, run.ExitCode);
        using var doc = JsonDocument.Parse(run.Stdout);
        var root = doc.RootElement;
        Assert.Equal(9, root.GetProperty("executed").GetInt32());
        Assert.Equal(0, root.GetProperty("blocked").GetInt32());
        Assert.Equal(1, root.GetProperty("pending").GetInt32());
        var write = root.GetProperty("tools")[0];
        Assert.Equal("Write", write.GetProperty("tool").GetString());
        Assert.Equal(119, write.GetProperty("totalMs").GetInt64());
    }

    [Fact]
    public void Blocked_lists_the_unmatched_pre_line()
    {
        var log = TempLog(
            """{"ts":"2026-10-04T10:00:00.000Z","event":"PreToolUse","id":"t1","session":"s1","mode":"auto","tool":"Write","path":".env"}""",
            """{"ts":"2026-10-04T10:00:01.000Z","event":"PreToolUse","id":"t2","session":"s1","mode":"auto","tool":"Read","path":"a.txt"}""",
            """{"ts":"2026-10-04T10:00:02.000Z","event":"PostToolUse","id":"t2","session":"s1","mode":"auto","tool":"Read","path":"a.txt","exit":0,"ms":1}""");
        try
        {
            var run = Agentlog("blocked", log);

            Assert.Equal(0, run.ExitCode);
            Assert.Equal(["ts", "tool", "target"], run.Row("ts"));
            Assert.Equal(2, run.Lines.Length);
            Assert.Equal("2026-10-04T10:00:00.000Z  Write  .env", run.Lines[1]);
        }
        finally
        {
            File.Delete(log);
        }
    }

    [Fact]
    public void Blocked_does_not_list_the_still_running_last_action()
    {
        Assert.Equal("No blocked actions.\n", Agentlog("blocked", Fixture).Stdout);
    }

    [Fact]
    public void Failed_lists_the_non_zero_exit()
    {
        var run = Agentlog("failed", Fixture);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(2, run.Lines.Length);
        var cells = run.Lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["2026-10-04T10:37:39.086Z", "Bash", "1", "cat"], cells[..4]);
    }

    [Fact]
    public void Failed_as_json_keeps_exit_shape()
    {
        var run = Agentlog("failed", Fixture, "--format", "json");

        using var doc = JsonDocument.Parse(run.Stdout);
        var failed = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal(1, failed.GetProperty("exit").GetInt32());
        Assert.Equal("PostToolUseFailure", failed.GetProperty("event").GetString());
    }

    [Fact]
    public void Json_keeps_null_keys_on_empty_log()
    {
        // Review 001 #4: consumers must see `null`, not a missing property.
        var empty = Path.GetTempFileName();
        try
        {
            var run = Agentlog("summary", empty, "--format", "json");

            Assert.Equal(0, run.ExitCode);
            using var doc = JsonDocument.Parse(run.Stdout);
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("first").ValueKind);
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("last").ValueKind);
            Assert.Equal(0, doc.RootElement.GetProperty("executed").GetInt32());
        }
        finally
        {
            File.Delete(empty);
        }
    }

    [Fact]
    public void Json_entries_keep_null_keys()
    {
        var run = Agentlog("failed", Fixture, "--format", "json");

        using var doc = JsonDocument.Parse(run.Stdout);
        var failed = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("path").ValueKind);
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("pattern").ValueKind);
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("url").ValueKind);
    }

    [Fact]
    public void Files_lists_paths_with_action_counts()
    {
        var run = Agentlog("files", Fixture);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["path", "actions", "tools"], run.Row("path"));
        Assert.Equal(["src/AgentLog.Core/LogLineParser.cs", "2", "Write"], run.Row("src/AgentLog.Core/LogLineParser.cs"));
        Assert.Equal(["AGENTS.md", "1", "Read"], run.Row("AGENTS.md"));
        Assert.Equal(5, run.Lines.Length);
    }

    [Fact]
    public void Since_filters_by_timestamp()
    {
        var run = Agentlog("summary", Fixture, "--since", "2026-10-04T11:00:00Z");

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith("Agent actions: 0 executed, 0 blocked, 1 pending, 0 failed; 1 session(s), 2026-10-04T11:20:40.969Z", run.Lines[0]);
        Assert.Equal(["Bash", "1", "0", "0", "0", "0.0", "0"], run.Row("Bash"));
    }

    [Fact]
    public void Session_filter_with_unknown_session_gives_empty_report()
    {
        var run = Agentlog("blocked", Fixture, "--session", "deadbeef");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("No blocked actions.\n", run.Stdout);
    }

    [Fact]
    public void Session_filter_with_known_session_keeps_everything()
    {
        Assert.Equal(Agentlog(Fixture).Stdout, Agentlog(Fixture, "--session", "1613274a").Stdout);
    }

    [Fact]
    public void Invalid_since_is_exit_1_with_message()
    {
        var run = Agentlog(Fixture, "--since", "soon");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("", run.Stdout);
        Assert.Contains("Invalid --since value 'soon'", run.Stderr);
    }

    [Fact]
    public void Missing_file_is_exit_2_with_hint_on_stderr()
    {
        var run = Agentlog("failed", "no/such/actions.jsonl");

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Stdout);
        Assert.Contains("No log at no/such/actions.jsonl", run.Stderr);
        Assert.Contains(".claude/settings.json", run.Stderr);
    }

    [Fact]
    public void Default_file_is_agent_log_in_working_directory()
    {
        var dir = Directory.CreateTempSubdirectory("agentlog-e2e-");
        try
        {
            Assert.Equal(2, AgentlogIn(dir.FullName).ExitCode);

            Directory.CreateDirectory(Path.Combine(dir.FullName, ".agent-log"));
            File.Copy(Fixture, Path.Combine(dir.FullName, ".agent-log", "actions.jsonl"));
            var run = AgentlogIn(dir.FullName);

            Assert.Equal(0, run.ExitCode);
            Assert.Equal(Agentlog(Fixture).Stdout, run.Stdout);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
