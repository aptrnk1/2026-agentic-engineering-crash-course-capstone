using System.CommandLine;
using AgentLog.Core;

namespace AgentLog.Cli;

/// <summary>Thin shell: parse args -> read file -> call Core -> print.</summary>
public static class CliApp
{
    public const string DefaultLogPath = ".agent-log/actions.jsonl";
    public const int MissingFile = 2;

    private delegate string Report(IEnumerable<string> lines, LogFilter filter, OutputFormat format);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, TimeProvider time)
    {
        Report summary = (lines, filter, format) => ReportFormatter.Summary(Aggregator.Summarize(lines, filter), format);

        var root = new RootCommand("Summarize the Claude Code agent log (.agent-log/actions.jsonl). Default command: summary.");
        Configure(root, summary, stdout, stderr, time);
        root.Subcommands.Add(Configure(
            new Command("summary", "Per-tool counts: proposed, executed, blocked, failed, time, files."),
            summary, stdout, stderr, time));
        root.Subcommands.Add(Configure(
            new Command("blocked", "Proposed actions that never ran (PreToolUse without a Post line of the same id)."),
            (lines, filter, format) => ReportFormatter.Blocked(Aggregator.Blocked(lines, filter), format),
            stdout, stderr, time));
        root.Subcommands.Add(Configure(
            new Command("failed", "Executed actions with a non-zero exit."),
            (lines, filter, format) => ReportFormatter.Failed(Aggregator.Failed(lines, filter), format),
            stdout, stderr, time));
        root.Subcommands.Add(Configure(
            new Command("files", "Files touched by executed actions."),
            (lines, filter, format) => ReportFormatter.Files(Aggregator.Files(lines, filter), format),
            stdout, stderr, time));

        return root.Parse(args).Invoke(new InvocationConfiguration { Output = stdout, Error = stderr });
    }

    private static Command Configure(Command command, Report report, TextWriter stdout, TextWriter stderr, TimeProvider time)
    {
        var file = new Argument<string>("file")
        {
            Description = "Path to the log.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => DefaultLogPath,
        };
        var session = new Option<string?>("--session") { Description = "Only this session id (8 chars, as in the log)." };
        var since = new Option<string?>("--since") { Description = "Only actions at or after this time: ISO 8601 (UTC if no offset) or 'today'." };
        var format = new Option<OutputFormat>("--format")
        {
            Description = "Output format.",
            DefaultValueFactory = _ => OutputFormat.Text,
        };
        command.Arguments.Add(file);
        command.Options.Add(session);
        command.Options.Add(since);
        command.Options.Add(format);

        command.SetAction(parse =>
        {
            DateTimeOffset? sinceValue = null;
            if (parse.GetValue(since) is { } raw)
            {
                if (!LogFilter.TryParseSince(raw, time, out var parsed))
                {
                    stderr.WriteLine($"Invalid --since value '{raw}'. Use an ISO 8601 date/time or 'today'.");
                    return 1;
                }
                sinceValue = parsed;
            }

            var path = parse.GetValue(file) ?? DefaultLogPath;
            if (!File.Exists(path))
            {
                stderr.WriteLine($"No log at {path}. Are the hooks in .claude/settings.json active? Run one Edit and check again.");
                return MissingFile;
            }

            var filter = new LogFilter(parse.GetValue(session), sinceValue);
            stdout.Write(report(File.ReadLines(path), filter, parse.GetValue(format)));
            return 0;
        });
        return command;
    }
}
