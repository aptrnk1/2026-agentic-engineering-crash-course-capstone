using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLog.Core;

public enum OutputFormat { Text, Json, Md }

/// <summary>Renders reports as text tables, Markdown or JSON. Output always ends with "\n".</summary>
public static class ReportFormatter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ExitStatusJsonConverter() },
    };

    public static string Summary(Summary s, OutputFormat format)
    {
        if (format == OutputFormat.Json)
            return ToJson(s);

        var header =
            $"Agent actions: {s.Executed} executed, {s.Blocked} blocked, {s.Failed} failed; " +
            $"{s.Sessions} session(s), {Ts(s.First)} .. {Ts(s.Last)}";
        if (s.SkippedLines > 0)
            header += $"; {s.SkippedLines} unparsable line(s) skipped";

        var table = Table(
            format,
            ["tool", "proposed", "executed", "blocked", "failed", "time (s)", "files"],
            [false, true, true, true, true, true, true],
            s.Tools.Select(t => new[]
            {
                t.Tool, N(t.Proposed), N(t.Executed), N(t.Blocked), N(t.Failed),
                (t.TotalMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture), N(t.UniqueFiles),
            }).ToList());

        return format == OutputFormat.Md
            ? $"**{header}**\n\n{table}"
            : $"{header}\n{table}";
    }

    public static string Blocked(IReadOnlyList<LogEntry> entries, OutputFormat format) =>
        format == OutputFormat.Json ? ToJson(entries)
        : entries.Count == 0 ? "No blocked actions.\n"
        : Table(
            format,
            ["ts", "tool", "target"],
            [false, false, false],
            entries.Select(e => new[] { Ts(e.Ts), e.Tool, Target(e) }).ToList());

    public static string Failed(IReadOnlyList<LogEntry> entries, OutputFormat format) =>
        format == OutputFormat.Json ? ToJson(entries)
        : entries.Count == 0 ? "No failed actions.\n"
        : Table(
            format,
            ["ts", "tool", "exit", "target"],
            [false, false, true, false],
            entries.Select(e => new[] { Ts(e.Ts), e.Tool, Exit(e.Exit), Target(e) }).ToList());

    public static string Files(IReadOnlyList<FileTouch> files, OutputFormat format) =>
        format == OutputFormat.Json ? ToJson(files)
        : files.Count == 0 ? "No files touched.\n"
        : Table(
            format,
            ["path", "actions", "tools"],
            [false, true, false],
            files.Select(f => new[] { f.Path, N(f.Actions), string.Join(", ", f.Tools) }).ToList());

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, Json) + "\n";

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Ts(DateTimeOffset? ts) =>
        ts?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) ?? "-";

    private static string Target(LogEntry e) => e.Cmd ?? e.Path ?? e.Pattern ?? e.Url ?? "";

    private static string Exit(ExitStatus? exit) => exit switch
    {
        null => "-",
        { Kind: ExitKind.Code, Code: var code } => code?.ToString(CultureInfo.InvariantCulture) ?? "-",
        { Kind: ExitKind.Interrupted } => "interrupted",
        _ => "error",
    };

    private static string Table(OutputFormat format, string[] headers, bool[] rightAlign, IReadOnlyList<string[]> rows)
    {
        // Multi-line commands would break a one-line-per-row table.
        var cells = rows.Select(r => r.Select(c => c.Replace("\r\n", "\\n").Replace("\n", "\\n")).ToArray()).ToList();
        var sb = new StringBuilder();

        if (format == OutputFormat.Md)
        {
            static string Md(string c) => c.Replace("|", "\\|");
            sb.Append("| ").AppendJoin(" | ", headers).Append(" |\n");
            sb.Append('|').AppendJoin("|", rightAlign.Select(r => r ? "---:" : "---")).Append("|\n");
            foreach (var row in cells)
                sb.Append("| ").AppendJoin(" | ", row.Select(Md)).Append(" |\n");
            return sb.ToString();
        }

        var widths = headers
            .Select((h, i) => cells.Select(r => r[i].Length).Prepend(h.Length).Max())
            .ToArray();
        foreach (var row in cells.Prepend(headers))
        {
            var line = string.Join("  ", row.Select((c, i) => rightAlign[i] ? c.PadLeft(widths[i]) : c.PadRight(widths[i])));
            sb.Append(line.TrimEnd()).Append('\n');
        }
        return sb.ToString();
    }
}
