namespace AgentLog.Core;

/// <summary>One line of .agent-log/actions.jsonl (format: .claude/hooks/log-action.mjs).</summary>
public sealed record LogEntry(
    DateTimeOffset Ts,
    string Event,
    string? Id,
    string Session,
    string? Mode,
    string Tool,
    string? Path = null,
    string? Cmd = null,
    string? Pattern = null,
    string? Url = null,
    ExitStatus? Exit = null,
    int? Ms = null);

public enum ExitKind { Code, Interrupted, Error }

/// <summary>`exit` is an int OR "interrupted" / "error".</summary>
public sealed record ExitStatus(ExitKind Kind, int? Code = null);

/// <summary>Either a parsed entry or the reason the line was skipped. Never both.</summary>
public sealed record ParseResult(LogEntry? Entry, string? SkipReason)
{
    public bool Skipped => Entry is null;
}
