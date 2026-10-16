using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Domain.Planning;
using LifeOS.Domain.Tasks;
using LifeOS.Tests.Infrastructure;

namespace LifeOS.Tests.Application;

public class SprintServiceTests
{
    private static readonly DateOnly Week = ServiceHarness.Week;
    private static readonly DateOnly NextWeek = ServiceHarness.NextWeek;

    private static async Task AssertCodeAsync(string code, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<PlanningException>(action);
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public async Task Board_for_a_week_without_a_sprint_is_empty_and_creates_nothing()
    {
        using var h = new ServiceHarness();
        var board = await h.Sprints.GetBoardAsync(new DateOnly(2026, 10, 16)); // Friday

        Assert.Equal(Week, board.WeekStart);
        Assert.Equal(42, board.IsoWeek);
        Assert.Null(board.SprintId);
        Assert.True(board.IsCurrent);
        Assert.Empty(board.Tasks);
        Assert.Equal(SprintScopeStats.Empty, board.Stats);
    }

    [Fact]
    public async Task Work_planned_before_the_week_starts_is_original_scope_and_later_work_is_added()
    {
        using var h = new ServiceHarness(now: "2026-10-11T12:00:00Z"); // Sunday, before local Monday 00:00
        var planned = await h.NewPlannableTaskAsync("Planerad");
        await h.Sprints.AddTaskAsync(Week, planned);

        h.Clock.Set("2026-10-14T09:00:00Z"); // Wednesday
        var late = await h.NewPlannableTaskAsync("Tillagd mitt i veckan");
        await h.Sprints.AddTaskAsync(Week, late);

        var stats = (await h.Sprints.GetBoardAsync(Week)).Stats;
        Assert.Equal(1, stats.Original);
        Assert.Equal(1, stats.AddedAfterStart);
        Assert.Equal(2, stats.Current);

        var log = await h.Sprints.GetScopeLogAsync(Week);
        Assert.Equal([false, true], log.Select(l => l.AfterBoundary));
    }

    [Fact]
    public async Task Week_boundary_is_local_midnight_not_utc_midnight()
    {
        // 22:30 UTC Sunday is already 00:30 Monday in Stockholm (CEST), so this is after the boundary.
        using var h = new ServiceHarness(now: "2026-10-11T22:30:00Z");
        var t = await h.NewPlannableTaskAsync("Strax efter midnatt");
        await h.Sprints.AddTaskAsync(Week, t);

        var stats = (await h.Sprints.GetBoardAsync(Week)).Stats;
        Assert.Equal(0, stats.Original);
        Assert.Equal(1, stats.AddedAfterStart);
    }

    [Fact]
    public async Task Explicit_start_moves_the_boundary_and_is_optional()
    {
        using var h = new ServiceHarness(now: "2026-10-12T08:00:00Z"); // Monday morning
        var a = await h.NewPlannableTaskAsync("A");
        await h.Sprints.AddTaskAsync(Week, a);

        // Without an explicit start both tasks planned on Monday count as added (boundary = Monday 00:00 local).
        h.Clock.Set("2026-10-12T09:00:00Z");
        await h.Sprints.CommitAsync(Week);
        h.Clock.Set("2026-10-12T10:00:00Z");
        var b = await h.NewPlannableTaskAsync("B");
        await h.Sprints.AddTaskAsync(Week, b);

        var board = await h.Sprints.GetBoardAsync(Week);
        Assert.True(board.IsCommitted);
        Assert.Equal(1, board.Stats.Original);      // A, planned before the explicit start
        Assert.Equal(1, board.Stats.AddedAfterStart); // B, planned after it
    }

    [Fact]
    public async Task Removing_a_task_keeps_it_in_the_backlog_and_in_the_history()
    {
        using var h = new ServiceHarness(now: "2026-10-11T12:00:00Z");
        var id = await h.NewPlannableTaskAsync("Ska bort");
        await h.Sprints.AddTaskAsync(Week, id);
        h.Clock.Set("2026-10-13T10:00:00Z");

        await h.Sprints.RemoveTaskAsync(Week, id);

        var board = await h.Sprints.GetBoardAsync(Week);
        var member = Assert.Single(board.Tasks);
        Assert.False(member.IsActive);
        Assert.Equal(ScopeChangeReason.Dropped, member.LeaveReason);
        Assert.Equal(1, board.Stats.Original);
        Assert.Equal(1, board.Stats.RemovedAfterStart);
        Assert.Equal(0, board.Stats.Current);

        var task = (await h.Tasks.GetAsync(id))!.Task;
        Assert.Equal(TaskTriage.Backlog, task.Triage);
        Assert.Null(task.PlannedWeekStart);
        Assert.Contains(id, (await h.Tasks.ListPlannableAsync()).Select(t => t.Id));
    }

    [Fact]
    public async Task A_removed_task_can_be_planned_again_without_a_duplicate_row()
    {
        using var h = new ServiceHarness();
        var id = await h.NewPlannableTaskAsync("Fram och tillbaka");
        await h.Sprints.AddTaskAsync(Week, id);
        await h.Sprints.RemoveTaskAsync(Week, id);
        await h.Sprints.AddTaskAsync(Week, id);

        var board = await h.Sprints.GetBoardAsync(Week);
        var member = Assert.Single(board.Tasks);
        Assert.True(member.IsActive);
        Assert.Equal(3, (await h.Sprints.GetScopeLogAsync(Week)).Count);
    }

    [Fact]
    public async Task Planning_the_same_task_twice_or_in_two_open_sprints_is_rejected()
    {
        using var h = new ServiceHarness();
        var id = await h.NewPlannableTaskAsync("En gång");
        await h.Sprints.AddTaskAsync(Week, id);

        await AssertCodeAsync(PlanningException.Codes.AlreadyInSprint, () => h.Sprints.AddTaskAsync(Week, id));
        await AssertCodeAsync(PlanningException.Codes.PlannedInOtherSprint, () => h.Sprints.AddTaskAsync(NextWeek, id));
    }

    [Fact]
    public async Task Only_open_top_level_tasks_can_be_planned()
    {
        using var h = new ServiceHarness();
        var parent = await h.NewPlannableTaskAsync("Förälder");
        var sub = await h.Tasks.AddSubtaskAsync(parent, "Del");
        var done = await h.NewPlannableTaskAsync("Klar");
        await h.Tasks.SetStatusAsync(done, TaskItemStatus.Done);

        await AssertCodeAsync(PlanningException.Codes.SubtaskNotPlannable, () => h.Sprints.AddTaskAsync(Week, sub));
        await AssertCodeAsync(PlanningException.Codes.TaskNotOpen, () => h.Sprints.AddTaskAsync(Week, done));
    }

    [Fact]
    public async Task Planning_an_inbox_task_moves_it_to_the_backlog()
    {
        using var h = new ServiceHarness();
        var id = await h.Tasks.CaptureAsync("Snabb tanke");
        await h.Sprints.AddTaskAsync(Week, id);

        var task = (await h.Tasks.GetAsync(id))!.Task;
        Assert.Equal(TaskTriage.Backlog, task.Triage);
        Assert.Equal(Week, task.PlannedWeekStart);
    }

    [Fact]
    public async Task Moving_a_task_between_weeks_is_logged_on_both_sprints()
    {
        using var h = new ServiceHarness(now: "2026-10-11T12:00:00Z");
        var id = await h.NewPlannableTaskAsync("Flyttas");
        await h.Sprints.AddTaskAsync(Week, id);
        h.Clock.Set("2026-10-13T10:00:00Z");

        await h.Sprints.MoveTaskAsync(id, NextWeek);

        var old = await h.Sprints.GetBoardAsync(Week);
        var next = await h.Sprints.GetBoardAsync(NextWeek);
        Assert.Equal(0, old.Stats.Current);
        Assert.Equal(1, old.Stats.RemovedAfterStart);
        Assert.Equal(1, next.Stats.Current);
        Assert.Equal(1, next.Stats.Original); // arrives well before next week's boundary
        Assert.Equal(NextWeek, (await h.Tasks.GetAsync(id))!.Task.PlannedWeekStart);
    }

    [Fact]
    public async Task Capacity_warns_but_never_blocks()
    {
        using var h = new ServiceHarness();
        await h.Sprints.SetCapacityAsync(Week, 120);
        var a = await h.NewPlannableTaskAsync("A", 60);
        var b = await h.NewPlannableTaskAsync("B", 50);
        var c = await h.NewPlannableTaskAsync("C", 30);

        await h.Sprints.AddTaskAsync(Week, a);
        Assert.Equal(CapacityStatus.Ok, (await h.Sprints.GetBoardAsync(Week)).Capacity);

        await h.Sprints.AddTaskAsync(Week, b);
        Assert.Equal(CapacityStatus.NearLimit, (await h.Sprints.GetBoardAsync(Week)).Capacity);

        await h.Sprints.AddTaskAsync(Week, c); // overplanned, still allowed
        var board = await h.Sprints.GetBoardAsync(Week);
        Assert.Equal(CapacityStatus.Over, board.Capacity);
        Assert.Equal(140, board.PlannedMinutes);
        Assert.Equal(3, board.Stats.Current);
    }

    [Fact]
    public async Task Invalid_capacity_is_rejected()
    {
        using var h = new ServiceHarness();
        await AssertCodeAsync(PlanningException.Codes.InvalidCapacity, () => h.Sprints.SetCapacityAsync(Week, 0));
        await h.Sprints.SetCapacityAsync(Week, null); // clearing is fine
    }

    [Fact]
    public async Task Goal_and_order_are_stored()
    {
        using var h = new ServiceHarness();
        var a = await h.NewPlannableTaskAsync("A");
        var b = await h.NewPlannableTaskAsync("B");
        await h.Sprints.AddTaskAsync(Week, a);
        await h.Sprints.AddTaskAsync(Week, b);
        await h.Sprints.SetGoalAsync(Week, "  Cykla tre gånger ");
        await h.Sprints.ReorderAsync(Week, [b, a]);

        var board = await h.Sprints.GetBoardAsync(Week);
        Assert.Equal("Cykla tre gånger", board.Goal);
        Assert.Equal(["B", "A"], board.ActiveTasks.Select(t => t.Task.Title));
    }

    [Fact]
    public async Task Closing_carries_unfinished_work_and_keeps_finished_work_in_the_closed_sprint()
    {
        using var h = new ServiceHarness(now: "2026-10-11T12:00:00Z");
        var done = await h.NewPlannableTaskAsync("Klar");
        var carried = await h.NewPlannableTaskAsync("Fortsätter");
        var back = await h.NewPlannableTaskAsync("Tillbaka");
        foreach (var id in new[] { done, carried, back })
        {
            await h.Sprints.AddTaskAsync(Week, id);
        }

        h.Clock.Set("2026-10-16T15:00:00Z");
        await h.Tasks.SetStatusAsync(done, TaskItemStatus.Done);
        await h.Tasks.SetStatusAsync(carried, TaskItemStatus.InProgress);

        h.Clock.Set("2026-10-18T18:00:00Z"); // Sunday evening, still before next week's boundary
        await h.Sprints.CloseAsync(Week, CarryDecision.NextSprint,
            new Dictionary<Guid, CarryDecision> { [back] = CarryDecision.Backlog });

        var old = await h.Sprints.GetBoardAsync(Week);
        Assert.True(old.IsClosed);
        Assert.Equal(1, old.Stats.CarriedOut);
        Assert.Equal(1, old.Stats.RemovedAfterStart);        // the one sent back to the backlog
        Assert.Equal(1, old.Stats.Current);                  // only the finished task remains
        Assert.Equal(ScopeChangeReason.CarryOver, old.Tasks.Single(t => t.Task.Id == carried).LeaveReason);
        Assert.Equal(ScopeChangeReason.ReturnedToBacklog, old.Tasks.Single(t => t.Task.Id == back).LeaveReason);
        Assert.True(old.Tasks.Single(t => t.Task.Id == done).IsActive);

        var next = await h.Sprints.GetBoardAsync(NextWeek);
        var arrived = Assert.Single(next.ActiveTasks);
        Assert.Equal(carried, arrived.Task.Id);
        Assert.Equal(TaskItemStatus.InProgress, arrived.Task.Status);
        Assert.Equal(1, next.Stats.CarriedIn);
        Assert.Equal(1, next.Stats.Original); // carried over before next week's boundary

        Assert.Equal(NextWeek, (await h.Tasks.GetAsync(carried))!.Task.PlannedWeekStart);
        Assert.Null((await h.Tasks.GetAsync(back))!.Task.PlannedWeekStart);
        Assert.Null((await h.Tasks.GetAsync(done))!.Task.PlannedWeekStart); // finished work is not "planned" any more
    }

    [Fact]
    public async Task Carry_over_after_the_next_boundary_counts_as_added_work_in_the_next_sprint()
    {
        using var h = new ServiceHarness(now: "2026-10-13T12:00:00Z");
        var id = await h.NewPlannableTaskAsync("Sen");
        await h.Sprints.AddTaskAsync(Week, id);

        h.Clock.Set("2026-10-20T08:00:00Z"); // Tuesday of next week
        await h.Sprints.CloseAsync(Week, CarryDecision.NextSprint);

        var next = await h.Sprints.GetBoardAsync(NextWeek);
        Assert.Equal(0, next.Stats.Original);
        Assert.Equal(1, next.Stats.AddedAfterStart);
        Assert.Equal(1, next.Stats.CarriedIn);
    }

    [Fact]
    public async Task A_closed_sprint_rejects_changes_and_closing_twice_is_harmless()
    {
        using var h = new ServiceHarness();
        var id = await h.NewPlannableTaskAsync("Något");
        await h.Sprints.AddTaskAsync(Week, id);
        await h.Sprints.CloseAsync(Week, CarryDecision.Backlog);
        await h.Sprints.CloseAsync(Week, CarryDecision.Backlog);

        var other = await h.NewPlannableTaskAsync("Nytt");
        await AssertCodeAsync(PlanningException.Codes.SprintClosed, () => h.Sprints.AddTaskAsync(Week, other));
        await AssertCodeAsync(PlanningException.Codes.SprintClosed, () => h.Sprints.SetGoalAsync(Week, "mål"));
        await AssertCodeAsync(PlanningException.Codes.SprintClosed, () => h.Sprints.SetCapacityAsync(Week, 60));
    }

    [Fact]
    public async Task Past_sprints_with_unfinished_work_are_suggested_for_closing()
    {
        using var h = new ServiceHarness(now: "2026-10-13T12:00:00Z");
        var open = await h.NewPlannableTaskAsync("Öppen");
        var finished = await h.NewPlannableTaskAsync("Klar");
        await h.Sprints.AddTaskAsync(Week, open);
        await h.Sprints.AddTaskAsync(Week, finished);
        await h.Tasks.SetStatusAsync(finished, TaskItemStatus.Done);

        Assert.Empty(await h.Sprints.ListPastSprintsToCloseAsync()); // current week is not "past"

        h.Clock.Set("2026-10-20T08:00:00Z");
        var hint = Assert.Single(await h.Sprints.ListPastSprintsToCloseAsync());
        Assert.Equal(Week, hint.WeekStart);
        Assert.Equal(1, hint.OpenTasks);
    }

    [Fact]
    public async Task Scope_changes_are_append_only()
    {
        using var h = new ServiceHarness();
        var id = await h.NewPlannableTaskAsync("Logg");
        await h.Sprints.AddTaskAsync(Week, id);

        await using var db = h.Db.CreateContext(h.User);
        var change = db.ScopeChanges.First();
        change.TaskTitle = "Ändrad";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        await using var db2 = h.Db.CreateContext(h.User);
        db2.ScopeChanges.Remove(db2.ScopeChanges.First());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Current_week_follows_the_users_local_date()
    {
        using var h = new ServiceHarness(now: "2026-10-18T21:30:00Z"); // Sunday 23:30 in Stockholm
        Assert.Equal(Week, h.Sprints.CurrentWeekStart);

        h.Clock.Set("2026-10-18T22:30:00Z"); // Monday 00:30 in Stockholm
        Assert.Equal(NextWeek, h.Sprints.CurrentWeekStart);
    }
}
