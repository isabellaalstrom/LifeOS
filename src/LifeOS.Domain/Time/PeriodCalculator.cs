namespace LifeOS.Domain.Time;

/// <summary>
/// Converts between UTC instants and user-local calendar periods. Events are stored in UTC;
/// weekly/monthly/quarterly/yearly boundaries are always evaluated in the user's time zone.
/// Weeks start on Monday and are identified by their Monday date.
/// </summary>
public static class PeriodCalculator
{
    public static DateOnly LocalDate(DateTimeOffset utcInstant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcInstant, zone).DateTime);

    public static DateOnly WeekStart(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    public static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    public static DateOnly QuarterStart(DateOnly date) => new(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1);

    public static DateOnly YearStart(DateOnly date) => new(date.Year, 1, 1);

    /// <summary>Half-open UTC range [startUtc, endUtc) covering local dates [startDate, endDateExclusive).</summary>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) ToUtcRange(
        DateOnly startDate, DateOnly endDateExclusive, TimeZoneInfo zone) =>
        (LocalMidnightToUtc(startDate, zone), LocalMidnightToUtc(endDateExclusive, zone));

    public static DateTimeOffset LocalMidnightToUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // A local midnight can fall in a DST gap in some zones; move to the first valid instant.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
