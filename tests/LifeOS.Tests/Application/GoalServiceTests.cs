using LifeOS.Application.Goals;
using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Tasks;
using LifeOS.Tests.Infrastructure;

namespace LifeOS.Tests.Application;

public class GoalServiceTests
{
    private static readonly DateOnly Oct2026 = new(2026, 10, 1);
    private static readonly DateOnly Year2026 = new(2026, 1, 1);

    private static async Task AssertCodeAsync(string code, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<PlanningException>(action);
        Assert.Equal(code, ex.Code);
    }

    private static GoalInput Numeric(string title = "FTP", decimal start = 200, decimal target = 280, decimal? current = null, string unit = "W") =>
        new(title, GoalHorizon.Quarter, Oct2026, GoalProgressMode.Numeric, StartValue: start, TargetValue: target, CurrentValue: current, Unit: unit);

    private static GoalInput Of(GoalProgressMode mode, string title = "Mål", GoalHorizon horizon = GoalHorizon.Quarter, DateOnly? start = null, Guid? parent = null) =>
        new(title, horizon, start ?? Oct2026, mode, ParentGoalId: parent);

    [Fact]
    public async Task A_numeric_goal_needs_start_and_target_and_defaults_current_to_start()
    {
        using var h = new ServiceHarness();
        await AssertCodeAsync(PlanningException.Codes.InvalidNumeric,
            () => h.Goals.CreateAsync(new GoalInput("X", GoalHorizon.Month, Oct2026, GoalProgressMode.Numeric, TargetValue: 10)));

        var id = await h.Goals.CreateAsync(Numeric());
        var goal = (await h.Goals.GetAsync(id))!.Goal;
        Assert.Equal(200m, goal.CurrentValue);
        Assert.Equal(0m, goal.Percent);
        Assert.Equal("W", goal.Unit);
    }

    [Fact]
    public async Task Updating_the_value_records_timestamped_history_with_value_and_percent()
    {
        using var h = new ServiceHarness();
        var id = await h.Goals.CreateAsync(Numeric());

        h.Clock.Set("2026-10-20T08:00:00Z");
        await h.Goals.SetNumericValueAsync(id, 240, "Efter testet");
        h.Clock.Set("2026-11-03T08:00:00Z");
        await h.Goals.SetNumericValueAsync(id, 260);

        var detail = (await h.Goals.GetAsync(id))!;
        Assert.Equal(75m, detail.Goal.Percent);
        Assert.Equal([0m, 50m, 75m], detail.History.Select(p => p.Percent));
        Assert.Equal([200m, 240m, 260m], detail.History.Select(p => p.Value));
        Assert.Equal(["Created", "Manual", "Manual"], detail.History.Select(p => p.Source.ToString()));
        Assert.Equal("Efter testet", detail.History[1].Note);
        Assert.True(detail.History[1].RecordedUtc < detail.History[2].RecordedUtc);
    }

    [Fact]
    public async Task A_decreasing_goal_progresses_as_the_value_falls_and_overachievement_is_flagged()
    {
        using var h = new ServiceHarness();
        var id = await h.Goals.CreateAsync(Numeric("Vikt", 90, 80, unit: "kg"));

        await h.Goals.SetNumericValueAsync(id, 85);
        Assert.Equal(50m, (await h.Goals.GetAsync(id))!.Goal.Percent);

        await h.Goals.SetNumericValueAsync(id, 78);
        var goal = (await h.Goals.GetAsync(id))!.Goal;
        Assert.Equal(100m, goal.Percent);
        Assert.True(goal.IsBeyondTarget);
    }

    [Fact]
    public async Task A_zero_distance_goal_is_safe()
    {
        using var h = new ServiceHarness();
        var id = await h.Goals.CreateAsync(Numeric("Behåll vikten", 70, 70, unit: "kg"));
        Assert.Equal(100m, (await h.Goals.GetAsync(id))!.Goal.Percent); // current = start = target

        await h.Goals.SetNumericValueAsync(id, 72);
        Assert.Equal(0m, (await h.Goals.GetAsync(id))!.Goal.Percent);
    }

    [Fact]
    public async Task Changing_the_target_recalculates_progress_but_history_keeps_the_target_it_had()
    {
        using var h = new ServiceHarness();
        var id = await h.Goals.CreateAsync(Numeric());
        await h.Goals.SetNumericValueAsync(id, 240); // 50% of 200 -> 280

        h.Clock.Set("2026-10-25T08:00:00Z");
        await h.Goals.UpdateAsync(id, Numeric(target: 320, current: 240)); // now 40/120 = 33.3%

        var detail = (await h.Goals.GetAsync(id))!;
        Assert.Equal(33.3m, detail.Goal.Percent);
        var last = detail.History.Last();
        Assert.Equal("TargetChanged", last.Source.ToString());
        Assert.Equal(320m, last.Target);
        Assert.Equal(280m, detail.History[1].Target); // earlier point still shows the old target
    }

    [Fact]
    public async Task Saving_without_changes_does_not_add_history()
    {
        using var h = new ServiceHarness();
        var id = await h.Goals.CreateAsync(Numeric());
        await h.Goals.UpdateAsync(id, Numeric());
        await h.Goals.UpdateAsync(id, Numeric());

        Assert.Single((await h.Goals.GetAsync(id))!.History);
    }

    [Fact]
    public async Task Manual_percent_is_validated_and_only_for_manual_goals()
    {
        using var h = new ServiceHarness();
        var manual = await h.Goals.CreateAsync(Of(GoalProgressMode.Manual));
        await AssertCodeAsync(PlanningException.Codes.InvalidPercent, () => h.Goals.SetManualPercentAsync(manual, 101));
        await AssertCodeAsync(PlanningException.Codes.InvalidPercent, () => h.Goals.SetManualPercentAsync(manual, -1));
        await h.Goals.SetManualPercentAsync(manual, 35, "Känns bra");
        Assert.Equal(35m, (await h.Goals.GetAsync(manual))!.Goal.Percent);

        var numeric = await h.Goals.CreateAsync(Numeric());
        await AssertCodeAsync(PlanningException.Codes.ModeMismatch, () => h.Goals.SetManualPercentAsync(numeric, 10));
        await AssertCodeAsync(PlanningException.Codes.ModeMismatch, () => h.Goals.SetNumericValueAsync(manual, 10));
    }

    [Fact]
    public async Task Milestone_progress_is_done_over_total_and_empty_is_zero_percent()
    {
        using var h = new ServiceHarness();
        var id = await h.Goals.CreateAsync(Of(GoalProgressMode.Milestones));
        Assert.Equal(0m, (await h.Goals.GetAsync(id))!.Goal.Percent);

        var a = await h.Goals.AddMilestoneAsync(id, "Anmäla mig");
        await h.Goals.AddMilestoneAsync(id, "Träna 8 veckor", new DateOnly(2026, 12, 1));
        await h.Goals.SetMilestoneDoneAsync(a, true);

        var detail = (await h.Goals.GetAsync(id))!;
        Assert.Equal(50m, detail.Goal.Percent);
        Assert.Equal((1, 2), (detail.Goal.MilestonesDone, detail.Goal.MilestonesTotal));
        Assert.Equal(["Anmäla mig", "Träna 8 veckor"], detail.Milestones.Select(m => m.Title));
        Assert.Contains(detail.History, p => p.Source == GoalProgressSource.Milestone && p.Percent == 50m);

        await h.Goals.SetMilestoneDoneAsync(a, false);
        Assert.Equal(0m, (await h.Goals.GetAsync(id))!.Goal.Percent);

        await h.Goals.DeleteMilestoneAsync(a);
        Assert.Single((await h.Goals.GetAsync(id))!.Milestones);
    }

    [Fact]
    public async Task Linked_task_progress_counts_the_goals_tasks_and_ignores_cancelled_and_subtasks()
    {
        using var h = new ServiceHarness();
        var goal = await h.Goals.CreateAsync(Of(GoalProgressMode.LinkedTasks));

        var direct = await h.Tasks.CreateAsync(new TaskInput("Direkt", GoalId: goal));
        var second = await h.Tasks.CreateAsync(new TaskInput("Andra", GoalId: goal));
        var cancelled = await h.Tasks.CreateAsync(new TaskInput("Avbruten", GoalId: goal));
        await h.Tasks.AddSubtaskAsync(direct, "Delmoment");
        await h.Tasks.SetStatusAsync(cancelled, TaskItemStatus.Cancelled);

        Assert.Equal(0m, (await h.Goals.GetAsync(goal))!.Goal.Percent);
        Assert.Equal((0, 2), ((await h.Goals.GetAsync(goal))!.Goal.TasksDone, (await h.Goals.GetAsync(goal))!.Goal.TasksTotal));

        await h.Tasks.SetStatusAsync(direct, TaskItemStatus.Done);
        Assert.Equal(50m, (await h.Goals.GetAsync(goal))!.Goal.Percent);

        await h.Tasks.SetStatusAsync(second, TaskItemStatus.Done);
        var detail = (await h.Goals.GetAsync(goal))!;
        Assert.Equal(100m, detail.Goal.Percent);
        Assert.Equal(2, detail.LinkedTasks.Count);
        Assert.Contains(detail.History, p => p.Source == GoalProgressSource.TaskChanged && p.Percent == 100m);
    }

    [Fact]
    public async Task Moving_a_task_between_goals_updates_both()
    {
        using var h = new ServiceHarness();
        var a = await h.Goals.CreateAsync(Of(GoalProgressMode.LinkedTasks, "A"));
        var b = await h.Goals.CreateAsync(Of(GoalProgressMode.LinkedTasks, "B"));
        var task = await h.Tasks.CreateAsync(new TaskInput("Flyttbar", GoalId: a));
        await h.Tasks.SetStatusAsync(task, TaskItemStatus.Done);
        Assert.Equal(100m, (await h.Goals.GetAsync(a))!.Goal.Percent);

        await h.Tasks.UpdateAsync(task, new TaskInput("Flyttbar", GoalId: b));
        Assert.Equal(0m, (await h.Goals.GetAsync(a))!.Goal.Percent);
        Assert.Equal(100m, (await h.Goals.GetAsync(b))!.Goal.Percent);

    }

    [Fact]
    public async Task Parents_must_have_a_longer_horizon_and_contain_the_child_period()
    {
        using var h = new ServiceHarness();
        var year = await h.Goals.CreateAsync(Of(GoalProgressMode.Manual, "År", GoalHorizon.Year, Year2026));
        var quarter = await h.Goals.CreateAsync(Of(GoalProgressMode.Manual, "Q4", GoalHorizon.Quarter, Oct2026, year));
        var month = await h.Goals.CreateAsync(Of(GoalProgressMode.Manual, "Nov", GoalHorizon.Month, new DateOnly(2026, 11, 15), quarter));

        Assert.Equal(new DateOnly(2026, 11, 1), (await h.Goals.GetAsync(month))!.Goal.PeriodStart); // normalized to the month start
        Assert.Equal(quarter, Assert.Single((await h.Goals.GetAsync(year))!.Children).Id);

        // wrong horizon order
        await AssertCodeAsync(PlanningException.Codes.InvalidParent,
            () => h.Goals.CreateAsync(Of(GoalProgressMode.Manual, "Fel", GoalHorizon.Year, Year2026, month)));
        // same horizon
        await AssertCodeAsync(PlanningException.Codes.InvalidParent,
            () => h.Goals.CreateAsync(Of(GoalProgressMode.Manual, "Fel", GoalHorizon.Quarter, Oct2026, quarter)));
        // outside the parent's period
        await AssertCodeAsync(PlanningException.Codes.InvalidParent,
            () => h.Goals.CreateAsync(Of(GoalProgressMode.Manual, "Fel", GoalHorizon.Quarter, new DateOnly(2027, 1, 1), year)));
        // own parent / unknown parent
        await AssertCodeAsync(PlanningException.Codes.InvalidParent, () => h.Goals.UpdateAsync(quarter, Of(GoalProgressMode.Manual, "Q4", parent: quarter)));
        await AssertCodeAsync(PlanningException.Codes.InvalidParent, () => h.Goals.CreateAsync(Of(GoalProgressMode.Manual, parent: Guid.NewGuid())));
        // a parent may not be turned into something shorter than its children
        await AssertCodeAsync(PlanningException.Codes.InvalidParent,
            () => h.Goals.UpdateAsync(quarter, Of(GoalProgressMode.Manual, "Q4", GoalHorizon.Month, Oct2026, year)));
    }

    [Fact]
    public async Task Deleting_a_goal_clears_links_and_is_refused_while_it_has_children()
    {
        using var h = new ServiceHarness();
        var year = await h.Goals.CreateAsync(Of(GoalProgressMode.LinkedTasks, "År", GoalHorizon.Year, Year2026));
        var child = await h.Goals.CreateAsync(Of(GoalProgressMode.Manual, "Q4", GoalHorizon.Quarter, Oct2026, year));
        var task = await h.Tasks.CreateAsync(new TaskInput("Länkad", GoalId: year));

        await AssertCodeAsync(PlanningException.Codes.HasChildren, () => h.Goals.DeleteAsync(year));

        await h.Goals.DeleteAsync(child);
        await h.Goals.DeleteAsync(year);
        Assert.Null(await h.Goals.GetAsync(year));
        Assert.Null((await h.Tasks.GetAsync(task))!.Task.GoalId);
    }

    [Fact]
    public async Task Status_can_be_set_by_the_owner_and_achieved_time_follows()
    {
        using var h = new ServiceHarness();
        var id = await h.Goals.CreateAsync(Numeric());
        await h.Goals.SetNumericValueAsync(id, 280);
        Assert.Equal(GoalStatus.Active, (await h.Goals.GetAsync(id))!.Goal.Status); // 100% is only a suggestion

        await h.Goals.SetStatusAsync(id, GoalStatus.Achieved);
        Assert.Equal(GoalStatus.Achieved, (await h.Goals.GetAsync(id))!.Goal.Status);
        Assert.Empty(await h.Goals.ListAsync());
        Assert.Single(await h.Goals.ListAsync(includeClosed: true));

        await h.Goals.SetStatusAsync(id, GoalStatus.Active);
        Assert.Single(await h.Goals.ListAsync());
    }

    [Fact]
    public async Task Lists_filter_by_horizon_and_period_and_current_goals_contain_today()
    {
        using var h = new ServiceHarness(); // 14 Oct 2026
        var q4 = await h.Goals.CreateAsync(Of(GoalProgressMode.Manual, "Q4", GoalHorizon.Quarter, Oct2026));
        await h.Goals.CreateAsync(Of(GoalProgressMode.Manual, "Q3", GoalHorizon.Quarter, new DateOnly(2026, 7, 1)));
        var year = await h.Goals.CreateAsync(Of(GoalProgressMode.Manual, "År", GoalHorizon.Year, Year2026));

        Assert.Equal([q4], (await h.Goals.ListAsync(GoalHorizon.Quarter, Oct2026)).Select(g => g.Id));
        Assert.Equal(2, (await h.Goals.ListAsync(GoalHorizon.Quarter)).Count);
        Assert.Equal([year, q4], (await h.Goals.ListCurrentAsync()).Select(g => g.Id));
    }

    [Fact]
    public async Task One_owner_never_sees_or_changes_another_owners_goals()
    {
        using var h = new ServiceHarness(userId: "user-a");
        var id = await h.Goals.CreateAsync(Numeric());
        var other = h.ForUser("user-b");

        Assert.Empty(await other.Goals.ListAsync(includeClosed: true));
        Assert.Null(await other.Goals.GetAsync(id));
        await AssertCodeAsync(PlanningException.Codes.NotFound, () => other.Goals.SetNumericValueAsync(id, 250));
        await AssertCodeAsync(PlanningException.Codes.NotFound, () => other.Goals.DeleteAsync(id));
        await AssertCodeAsync(PlanningException.Codes.InvalidParent,
            () => other.Goals.CreateAsync(Of(GoalProgressMode.Manual, parent: id)));
    }

    [Fact]
    public async Task Goal_history_cannot_be_orphaned_by_other_owners_writes()
    {
        using var h = new ServiceHarness(userId: "user-a");
        var id = await h.Goals.CreateAsync(Of(GoalProgressMode.Manual));
        await h.Goals.SetManualPercentAsync(id, 40);

        var other = h.ForUser("user-b");
        await AssertCodeAsync(PlanningException.Codes.NotFound, () => other.Goals.SetManualPercentAsync(id, 99));
        Assert.Equal(40m, (await h.Goals.GetAsync(id))!.Goal.Percent);
        Assert.Equal(2, (await h.Goals.GetAsync(id))!.History.Count);
    }
}
