namespace AgentLog.Core.Tests;

public class LogFilterTests
{
    private sealed class FixedTime(DateTimeOffset utcNow, TimeZoneInfo zone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => zone;
    }

    private static readonly TimeZoneInfo Kyiv =
        TimeZoneInfo.CreateCustomTimeZone("test+3", TimeSpan.FromHours(3), "test+3", "test+3");

    [Fact]
    public void Today_is_start_of_local_day_of_the_time_provider()
    {
        // 2026-10-04 23:30 UTC is already 2026-10-05 02:30 at +03:00.
        var time = new FixedTime(new DateTimeOffset(2026, 10, 4, 23, 30, 0, TimeSpan.Zero), Kyiv);

        Assert.True(LogFilter.TryParseSince("today", time, out var since));
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(3)), since);
    }

    [Theory]
    [InlineData("2026-10-04T11:00:00Z", "2026-10-04T11:00:00+00:00")]
    [InlineData("2026-10-04T14:00:00+03:00", "2026-10-04T11:00:00+00:00")]
    [InlineData("2026-10-04", "2026-10-04T00:00:00+00:00")]
    public void Iso_values_are_parsed_as_utc_unless_offset_given(string value, string expected)
    {
        Assert.True(LogFilter.TryParseSince(value, TimeProvider.System, out var since));
        Assert.Equal(DateTimeOffset.Parse(expected), since);
    }

    [Fact]
    public void Garbage_since_is_rejected()
    {
        Assert.False(LogFilter.TryParseSince("yesterday-ish", TimeProvider.System, out _));
    }
}
