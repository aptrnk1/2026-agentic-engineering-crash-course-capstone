using System.Globalization;

namespace AgentLog.Core;

/// <summary>Which log lines to keep. Null fields do not filter.</summary>
/// <param name="Session">Exact session id as written by the hook (first 8 chars).</param>
/// <param name="Since">Keep lines with <c>ts</c> at or after this moment.</param>
public sealed record LogFilter(string? Session = null, DateTimeOffset? Since = null)
{
    public bool Matches(LogEntry entry) =>
        (Session is null || string.Equals(entry.Session, Session, StringComparison.Ordinal))
        && (Since is null || entry.Ts >= Since);

    /// <summary>
    /// Parses a --since value: "today" = start of the current local day of <paramref name="time"/>;
    /// otherwise an ISO 8601 date/time, read as UTC when it has no offset.
    /// </summary>
    public static bool TryParseSince(string value, TimeProvider time, out DateTimeOffset since)
    {
        if (string.Equals(value, "today", StringComparison.OrdinalIgnoreCase))
        {
            var today = time.GetLocalNow().Date;
            since = new DateTimeOffset(today, time.LocalTimeZone.GetUtcOffset(today));
            return true;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out since);
    }
}
