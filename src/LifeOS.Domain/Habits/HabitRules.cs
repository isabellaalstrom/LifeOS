using LifeOS.Domain.Time;

namespace LifeOS.Domain.Habits;

/// <summary>Period arithmetic and consistency for habits. Weeks start on Monday.</summary>
public static class HabitRules
{
    public static DateOnly PeriodStart(HabitPeriod period, DateOnly date) => period switch
    {
        HabitPeriod.Day => date,
        HabitPeriod.Week => PeriodCalculator.WeekStart(date),
        HabitPeriod.Month => PeriodCalculator.MonthStart(date),
        _ => throw new ArgumentOutOfRangeException(nameof(period)),
    };

    public static DateOnly Next(HabitPeriod period, DateOnly periodStart) => period switch
    {
        HabitPeriod.Day => periodStart.AddDays(1),
        HabitPeriod.Week => periodStart.AddDays(7),
        HabitPeriod.Month => periodStart.AddMonths(1),
        _ => throw new ArgumentOutOfRangeException(nameof(period)),
    };

    public static DateOnly Previous(HabitPeriod period, DateOnly periodStart) => period switch
    {
        HabitPeriod.Day => periodStart.AddDays(-1),
        HabitPeriod.Week => periodStart.AddDays(-7),
        HabitPeriod.Month => periodStart.AddMonths(-1),
        _ => throw new ArgumentOutOfRangeException(nameof(period)),
    };

    public static int CountInPeriod(IEnumerable<DateOnly> completionDates, HabitPeriod period, DateOnly periodStart)
    {
        var end = Next(period, periodStart);
        return completionDates.Count(d => d >= periodStart && d < end);
    }

    /// <summary>
    /// Number of consecutive periods in which the target was met. The period in progress counts when it is already met,
    /// but an unfinished current period never breaks the streak: counting then starts from the previous period.
    /// </summary>
    public static int Streak(IReadOnlyCollection<DateOnly> completionDates, HabitPeriod period, int target, DateOnly today)
    {
        if (target <= 0)
        {
            return 0;
        }

        var cursor = PeriodStart(period, today);
        var streak = 0;

        if (CountInPeriod(completionDates, period, cursor) >= target)
        {
            streak++;
        }

        cursor = Previous(period, cursor);

        // Bounded look-back keeps this cheap even for very old habits.
        for (var i = 0; i < 520; i++)
        {
            if (CountInPeriod(completionDates, period, cursor) < target)
            {
                break;
            }

            streak++;
            cursor = Previous(period, cursor);
        }

        return streak;
    }
}
