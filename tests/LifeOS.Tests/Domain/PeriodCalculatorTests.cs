using LifeOS.Domain.Time;

namespace LifeOS.Tests.Domain;

public class PeriodCalculatorTests
{
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    [Theory]
    [InlineData("2026-10-12", "2026-10-12")] // Monday
    [InlineData("2026-10-14", "2026-10-12")] // Wednesday
    [InlineData("2026-10-18", "2026-10-12")] // Sunday belongs to the week that started on Monday
    [InlineData("2027-01-01", "2026-12-28")] // week spanning a year boundary
    public void WeekStart_is_the_preceding_monday(string date, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), PeriodCalculator.WeekStart(DateOnly.Parse(date)));

    [Theory]
    [InlineData("2026-01-31", "2026-01-01")]
    [InlineData("2026-03-31", "2026-01-01")]
    [InlineData("2026-04-01", "2026-04-01")]
    [InlineData("2026-12-31", "2026-10-01")]
    public void QuarterStart_snaps_to_first_month_of_quarter(string date, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), PeriodCalculator.QuarterStart(DateOnly.Parse(date)));

    [Fact]
    public void MonthStart_and_YearStart_snap_to_first_day()
    {
        var date = new DateOnly(2026, 8, 19);
        Assert.Equal(new DateOnly(2026, 8, 1), PeriodCalculator.MonthStart(date));
        Assert.Equal(new DateOnly(2026, 1, 1), PeriodCalculator.YearStart(date));
    }

    [Fact]
    public void LocalDate_uses_the_user_time_zone_not_utc()
    {
        // 22:30 UTC on 31 March is already 1 April in Stockholm (CEST, UTC+2).
        var instant = new DateTimeOffset(2026, 3, 31, 22, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 4, 1), PeriodCalculator.LocalDate(instant, Stockholm));
    }

    [Fact]
    public void ToUtcRange_handles_spring_forward_week_as_167_hours()
    {
        // DST starts Sunday 29 March 2026 in Sweden.
        var (start, end) = PeriodCalculator.ToUtcRange(new DateOnly(2026, 3, 23), new DateOnly(2026, 3, 30), Stockholm);
        Assert.Equal(new DateTimeOffset(2026, 3, 22, 23, 0, 0, TimeSpan.Zero), start); // CET, UTC+1
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 22, 0, 0, TimeSpan.Zero), end);   // CEST, UTC+2
        Assert.Equal(TimeSpan.FromHours(167), end - start);
    }

    [Fact]
    public void ToUtcRange_handles_fall_back_week_as_169_hours()
    {
        // DST ends Sunday 25 October 2026 in Sweden.
        var (start, end) = PeriodCalculator.ToUtcRange(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 26), Stockholm);
        Assert.Equal(TimeSpan.FromHours(169), end - start);
    }

    [Fact]
    public void LocalMidnightToUtc_moves_past_a_dst_gap_when_midnight_does_not_exist()
    {
        // Sao Paulo historically skipped midnight at DST start (e.g. 2018-11-04 00:00 -> 01:00).
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var utc = PeriodCalculator.LocalMidnightToUtc(new DateOnly(2018, 11, 4), zone);
        Assert.Equal(new DateOnly(2018, 11, 4), PeriodCalculator.LocalDate(utc, zone));
    }
}
