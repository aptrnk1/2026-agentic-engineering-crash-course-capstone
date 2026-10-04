using System.Text.Json;

namespace AgentLog.Core;

public static class LogLineParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ExitStatusJsonConverter() },
    };

    /// <summary>Parses one log line. Never throws on bad input: returns a skipped result instead.</summary>
    public static ParseResult Parse(string line)
    {
        LogEntry? entry;
        try
        {
            entry = JsonSerializer.Deserialize<LogEntry>(line, Options);
        }
        catch (JsonException ex)
        {
            return Skip($"invalid JSON: {ex.Message}");
        }

        if (entry is null)
            return Skip("line is not a JSON object");
        if (entry.Ts == default || entry.Event is null || entry.Session is null || entry.Tool is null)
            return Skip("missing required field (ts, event, session or tool)");

        return new ParseResult(entry, null);
    }

    private static ParseResult Skip(string reason) => new(null, reason);
}
