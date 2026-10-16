using LifeOS.Domain.Goals;
using LifeOS.Domain.Habits;

namespace LifeOS.Tests.Domain;

public class GoalAndHabitRulesTests
{
    [Theory]
    [InlineData(200, 280, 240, 50)]   // increasing (e.g. FTP in watts)
    [InlineData(200, 280, 200, 0)]
    [InlineData(200, 280, 280, 100)]
    [InlineData(200, 280, 300, 100)]  // beyond the target is capped
    [InlineData(200, 280, 150, 0)]    // moving away from the target is floored
    [InlineData(90, 80, 85, 50)]      // decreasing target (e.g. weight)
    [InlineData(90, 80, 90, 0)]
    [InlineData(90, 80, 78, 100)]
    [InlineData(90, 80, 95, 0)]
    public void Numeric_progress_works_in_both_directions_and_is_bounded(double start, double target, double current, double expected) =>
        Assert.Equal((decimal)expected, GoalProgressCalculator.Numeric((decimal)start, (decimal)target, (decimal)current));

    [Theory]
    [InlineData(10, 10, 10, 100)]
    [InlineData(10, 10, 9, 0)]
    [InlineData(10, 10, 11, 0)]
    public void Zero_distance_goal_is_complete_only_when_the_value_equals_the_target(double start, double target, double current, double expected) =>
        Assert.Equal((decimal)expected, GoalProgressCalculator.Numeric((decimal)start, (decimal)target, (decimal)current));

    [Fact]
    public void Beyond_target_is_detected_for_increasing_and_decreasing_goals()
    {
        Assert.True(GoalProgressCalculator.IsBeyondTarget(200, 280, 281));
        Assert.False(GoalProgressCalculator.IsBeyondTarget(200, 280, 280));
        Assert.True(GoalProgressCalculator.IsBeyondTarget(90, 80, 79));
        Assert.False(GoalProgressCalculator.IsBeyondTarget(90, 80, 85));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 5, 0)]
    [InlineData(2, 4, 50)]
    [InlineData(4, 4, 100)]
    [InlineData(6, 4, 100)]
    public void Ratio_handles_empty_sets(int done, int total, double expected) =>
        Assert.Equal((decimal)expected, GoalProgressCalculator.Ratio(done, total));

    [Fact]
    public void Goal_progress_follows_the_mode_and_never_throws_on_missing_data()
    {
        var numeric = new Goal { Mode = GoalProgressMode.Numeric };
        Assert.Equal(0m, GoalProgressCalculator.ForGoal(numeric, 0, 0, 0, 0)); // no start/target yet

        numeric.StartValue = 0;
        numeric.TargetValue = 10;
        Assert.Equal(0m, GoalProgressCalculator.ForGoal(numeric, 0, 0, 0, 0)); // current defaults to start
        numeric.CurrentValue = 4;
        Assert.Equal(40m, GoalProgressCalculator.ForGoal(numeric, 0, 0, 0, 0));

        Assert.Equal(50m, GoalProgressCalculator.ForGoal(new Goal { Mode = GoalProgressMode.Milestones }, 1, 2, 0, 0));
        Assert.Equal(75m, GoalProgressCalculator.ForGoal(new Goal { Mode = GoalProgressMode.LinkedTasks }, 0, 0, 3, 4));
        Assert.Equal(100m, GoalProgressCalculator.ForGoal(new Goal { Mode = GoalProgressMode.Manual, ManualPercent = 140 }, 0, 0, 0, 0));
    }

    [Theory]
    [InlineData(GoalHorizon.Year, "2026-10-16", "2026-01-01", "2027-01-01")]
    [InlineData(GoalHorizon.Quarter, "2026-10-16", "2026-10-01", "2027-01-01")]
    [InlineData(GoalHorizon.Quarter, "2026-03-31", "2026-01-01", "2026-04-01")]
    [InlineData(GoalHorizon.Month, "2026-02-15", "2026-02-01", "2026-03-01")]
    public void Periods_are_calendar_years_quarters_and_months(GoalHorizon horizon, string date, string start, string end)
    {
        var s = GoalPeriods.Start(horizon, DateOnly.Parse(date));
        Assert.Equal(DateOnly.Parse(start), s);
        Assert.Equal(DateOnly.Parse(end), GoalPeriods.EndExclusive(horizon, s));
    }

    [Fact]
    public void Child_goals_must_be_shorter_and_inside_the_parent_period()
    {
        var year = new DateOnly(2026, 1, 1);
        Assert.True(GoalPeriods.IsValidParentHorizon(GoalHorizon.Year, GoalHorizon.Quarter));
        Assert.True(GoalPeriods.IsValidParentHorizon(GoalHorizon.Quarter, GoalHorizon.Month));
        Assert.False(GoalPeriods.IsValidParentHorizon(GoalHorizon.Month, GoalHorizon.Year));
        Assert.False(GoalPeriods.IsValidParentHorizon(GoalHorizon.Year, GoalHorizon.Year));

        Assert.True(GoalPeriods.IsInside(GoalHorizon.Year, year, GoalHorizon.Quarter, new DateOnly(2026, 10, 1)));
        Assert.False(GoalPeriods.IsInside(GoalHorizon.Year, year, GoalHorizon.Quarter, new DateOnly(2027, 1, 1)));
        Assert.True(GoalPeriods.IsInside(GoalHorizon.Quarter, new DateOnly(2026, 10, 1), GoalHorizon.Month, new DateOnly(2026, 12, 1)));
        Assert.False(GoalPeriods.IsInside(GoalHorizon.Quarter, new DateOnly(2026, 10, 1), GoalHorizon.Month, new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Shift_moves_whole_periods()
    {
        Assert.Equal(new DateOnly(2027, 1, 1), GoalPeriods.Shift(GoalHorizon.Quarter, new DateOnly(2026, 10, 1), 1));
        Assert.Equal(new DateOnly(2026, 7, 1), GoalPeriods.Shift(GoalHorizon.Quarter, new DateOnly(2026, 10, 1), -1));
        Assert.Equal(new DateOnly(2025, 1, 1), GoalPeriods.Shift(GoalHorizon.Year, new DateOnly(2026, 1, 1), -1));
    }

    [Fact]
    public void Habit_periods_start_on_monday_and_first_of_month()
    {
        Assert.Equal(new DateOnly(2026, 10, 12), HabitRules.PeriodStart(HabitPeriod.Week, new DateOnly(2026, 10, 18)));
        Assert.Equal(new DateOnly(2026, 10, 1), HabitRules.PeriodStart(HabitPeriod.Month, new DateOnly(2026, 10, 18)));
        Assert.Equal(new DateOnly(2026, 10, 18), HabitRules.PeriodStart(HabitPeriod.Day, new DateOnly(2026, 10, 18)));
    }

    [Fact]
    public void Completions_are_counted_per_period()
    {
        var dates = new[] { new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 19) };
        Assert.Equal(3, HabitRules.CountInPeriod(dates, HabitPeriod.Week, new DateOnly(2026, 10, 12)));
        Assert.Equal(1, HabitRules.CountInPeriod(dates, HabitPeriod.Week, new DateOnly(2026, 10, 5)));
        Assert.Equal(2, HabitRules.CountInPeriod(dates, HabitPeriod.Day, new DateOnly(2026, 10, 14)) );
    }

    [Fact]
    public void Streak_counts_consecutive_met_periods_and_an_unfinished_current_period_never_breaks_it()
    {
        var today = new DateOnly(2026, 10, 14); // Wednesday
        DateOnly[] Week(DateOnly monday, int n) => Enumerable.Range(0, n).Select(i => monday.AddDays(i)).ToArray();

        // Two earlier weeks met (target 3), current week only 1 so far: streak is 2, not 0.
        var dates = Week(new DateOnly(2026, 9, 28), 3).Concat(Week(new DateOnly(2026, 10, 5), 3)).Concat(Week(new DateOnly(2026, 10, 12), 1)).ToArray();
        Assert.Equal(2, HabitRules.Streak(dates, HabitPeriod.Week, 3, today));

        // Once the current week is met it counts too.
        var met = dates.Concat(new[] { new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 14) }).ToArray();
        Assert.Equal(3, HabitRules.Streak(met, HabitPeriod.Week, 3, today));

        // A missed week in the middle ends the streak.
        var gap = Week(new DateOnly(2026, 9, 21), 3).Concat(Week(new DateOnly(2026, 10, 5), 3)).ToArray();
        Assert.Equal(1, HabitRules.Streak(gap, HabitPeriod.Week, 3, today));

        Assert.Equal(0, HabitRules.Streak([], HabitPeriod.Week, 3, today));
        Assert.Equal(0, HabitRules.Streak(dates, HabitPeriod.Week, 0, today));
    }

    [Fact]
    public void Daily_streak_counts_days_in_a_row()
    {
        var today = new DateOnly(2026, 10, 14);
        var dates = new[] { new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 10) };
        Assert.Equal(3, HabitRules.Streak(dates, HabitPeriod.Day, 1, today));
        Assert.Equal(2, HabitRules.Streak(new[] { new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 12) }, HabitPeriod.Day, 1, today));
    }
}
