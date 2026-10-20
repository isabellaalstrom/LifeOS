using LifeOS.Application.Goals;
using LifeOS.Application.Habits;
using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Habits;
using LifeOS.Domain.Tasks;
using LifeOS.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Tests.Application;

public class HabitServiceTests
{
    private static readonly DateOnly Week = ServiceHarness.Week;          // Mon 12 Oct 2026
    private static readonly DateOnly Wednesday = new(2026, 10, 14);       // "today" in the harness

    private static async Task AssertCodeAsync(string code, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<PlanningException>(action);
        Assert.Equal(code, ex.Code);
    }

    private static HabitInput Weekly(string name = "Cykla", int target = 3, Guid? goal = null, decimal increment = 1m) =>
        new(name, HabitPeriod.Week, target, GoalId: goal, GoalIncrement: increment, EstimateMinutes: 60);

    [Fact]
    public async Task Habit_input_is_validated()
    {
        using var h = new ServiceHarness();
        await AssertCodeAsync(PlanningException.Codes.NameRequired, () => h.Habits.CreateAsync(Weekly(" ")));
        await AssertCodeAsync(PlanningException.Codes.InvalidTarget, () => h.Habits.CreateAsync(Weekly(target: 0)));
        await AssertCodeAsync(PlanningException.Codes.InvalidTarget, () => h.Habits.CreateAsync(Weekly(target: 101)));
        await AssertCodeAsync(PlanningException.Codes.InvalidEstimate,
            () => h.Habits.CreateAsync(new HabitInput("X", HabitPeriod.Week, 1, EstimateMinutes: 0)));
    }

    [Fact]
    public async Task Only_weekly_habits_generate_week_tasks()
    {
        using var h = new ServiceHarness();
        var daily = await h.Habits.CreateAsync(new HabitInput("Stretcha", HabitPeriod.Day, 1));
        var monthly = await h.Habits.CreateAsync(new HabitInput("Säkerhetskopiera", HabitPeriod.Month, 1));
        var weekly = await h.Habits.CreateAsync(Weekly());

        Assert.False((await h.Habits.GetAsync(daily))!.Habit.GenerateSprintTasks);
        Assert.False((await h.Habits.GetAsync(monthly))!.Habit.GenerateSprintTasks);
        Assert.True((await h.Habits.GetAsync(weekly))!.Habit.GenerateSprintTasks);
    }

    [Fact]
    public async Task Logging_counts_completions_in_the_current_period_and_unlogging_removes_the_latest()
    {
        using var h = new ServiceHarness();
        var id = await h.Habits.CreateAsync(Weekly());

        await h.Habits.LogAsync(id);
        await h.Habits.LogAsync(id, new DateOnly(2026, 10, 12), "Måndagspass");
        Assert.Equal((2, false), ((await h.Habits.GetAsync(id))!.Habit.CountInPeriod, (await h.Habits.GetAsync(id))!.Habit.TargetMet));

        await h.Habits.LogAsync(id);
        var habit = (await h.Habits.GetAsync(id))!.Habit;
        Assert.Equal(3, habit.CountInPeriod);
        Assert.True(habit.TargetMet);

        await h.Habits.UnlogAsync(id, Wednesday);
        Assert.Equal(2, (await h.Habits.GetAsync(id))!.Habit.CountInPeriod);
        Assert.Equal(1, (await h.Habits.GetAsync(id))!.Completions.Count(c => c.Date == Wednesday));
    }

    [Fact]
    public async Task Future_dates_are_rejected_and_unlogging_nothing_reports_not_found()
    {
        using var h = new ServiceHarness();
        var id = await h.Habits.CreateAsync(Weekly());
        await AssertCodeAsync(PlanningException.Codes.FutureDate, () => h.Habits.LogAsync(id, Wednesday.AddDays(1)));
        await AssertCodeAsync(PlanningException.Codes.NotFound, () => h.Habits.UnlogAsync(id, Wednesday));
    }

    [Fact]
    public async Task Consistency_counts_met_weeks_and_an_unfinished_week_is_not_a_failure()
    {
        using var h = new ServiceHarness(); // Wednesday 14 Oct
        var id = await h.Habits.CreateAsync(Weekly());
        foreach (var d in new[] { "2026-09-29", "2026-09-30", "2026-10-01", "2026-10-06", "2026-10-07", "2026-10-08", "2026-10-12" })
        {
            await h.Habits.LogAsync(id, DateOnly.Parse(d));
        }

        var detail = (await h.Habits.GetAsync(id))!;
        Assert.Equal(2, detail.Habit.Streak);                 // two met weeks; the current one (1 of 3) does not break it
        Assert.Equal(8, detail.RecentPeriods.Count);
        Assert.Equal(1, detail.RecentPeriods[^1].Count);       // this week so far
        Assert.False(detail.RecentPeriods[^1].Met);
        Assert.True(detail.RecentPeriods[^2].Met);
    }

    [Fact]
    public async Task Completions_contribute_to_a_linked_numeric_goal_and_unlogging_takes_them_back()
    {
        using var h = new ServiceHarness();
        var goal = await h.Goals.CreateAsync(new GoalInput("100 pass", GoalHorizon.Year, new DateOnly(2026, 1, 1), GoalProgressMode.Numeric,
            StartValue: 0, TargetValue: 100, Unit: "pass"));
        var habit = await h.Habits.CreateAsync(Weekly(goal: goal, increment: 2m));

        await h.Habits.LogAsync(habit);
        await h.Habits.LogAsync(habit, new DateOnly(2026, 10, 12));
        var detail = (await h.Goals.GetAsync(goal))!;
        Assert.Equal(4m, detail.Goal.CurrentValue);
        Assert.Equal(4m, detail.Goal.Percent);
        Assert.Contains(detail.History, p => p.Source == GoalProgressSource.Habit && p.Value == 4m);

        await h.Habits.UnlogAsync(habit, Wednesday);
        Assert.Equal(2m, (await h.Goals.GetAsync(goal))!.Goal.CurrentValue);
    }

    [Fact]
    public async Task A_habit_never_adds_to_a_goal_that_is_driven_by_linked_tasks()
    {
        using var h = new ServiceHarness();
        var goal = await h.Goals.CreateAsync(new GoalInput("Mål", GoalHorizon.Quarter, new DateOnly(2026, 10, 1), GoalProgressMode.LinkedTasks));
        var habit = await h.Habits.CreateAsync(Weekly(goal: goal));

        await h.Habits.LogAsync(habit);
        var g = (await h.Goals.GetAsync(goal))!.Goal;
        Assert.Null(g.CurrentValue);
        Assert.Equal(0m, g.Percent); // no linked tasks yet; the plain log did not count for the goal
    }

    [Fact]
    public async Task Generating_the_week_creates_one_task_per_occurrence_and_plans_them()
    {
        using var h = new ServiceHarness();
        var area = await h.Areas.CreateAsync("Cykling");
        var habit = await h.Habits.CreateAsync(new HabitInput("Cykla", HabitPeriod.Week, 3, LifeAreaId: area, EstimateMinutes: 90));

        var result = await h.Habits.GenerateWeekTasksAsync(Wednesday);
        Assert.Equal((3, 3), (result.Created, result.AddedToSprint));

        var board = await h.Sprints.GetBoardAsync(Week);
        Assert.Equal(["Cykla (1/3)", "Cykla (2/3)", "Cykla (3/3)"], board.ActiveTasks.Select(t => t.Task.Title));
        Assert.All(board.ActiveTasks, t =>
        {
            Assert.Equal(90, t.Task.EstimateMinutes);
            Assert.Equal(area, t.Task.LifeAreaId);
            Assert.Equal(habit, t.Task.HabitId);
        });
        Assert.Equal(270, board.PlannedMinutes);
    }

    [Fact]
    public async Task Generating_twice_never_creates_duplicates()
    {
        using var h = new ServiceHarness();
        await h.Habits.CreateAsync(Weekly());

        await h.Habits.GenerateWeekTasksAsync(Wednesday);
        var second = await h.Habits.GenerateWeekTasksAsync(Wednesday);
        var third = await h.Habits.GenerateWeekTasksAsync(Week);

        Assert.Equal((0, 0), (second.Created, second.AddedToSprint));
        Assert.Equal(0, third.Created);
        Assert.Equal(3, (await h.Sprints.GetBoardAsync(Week)).ActiveTasks.Count());
    }

    [Fact]
    public async Task Completions_already_logged_this_week_reduce_how_many_tasks_are_generated()
    {
        using var h = new ServiceHarness();
        var id = await h.Habits.CreateAsync(Weekly());
        await h.Habits.LogAsync(id); // one ride already done this week

        var result = await h.Habits.GenerateWeekTasksAsync(Wednesday);
        Assert.Equal(2, result.Created);
        await h.Habits.GenerateWeekTasksAsync(Wednesday);
        Assert.Equal(2, (await h.Sprints.GetBoardAsync(Week)).ActiveTasks.Count());
    }

    [Fact]
    public async Task A_failed_run_is_repaired_by_the_next_one_and_each_week_gets_its_own_tasks()
    {
        using var h = new ServiceHarness();
        await h.Habits.CreateAsync(Weekly(target: 2));
        await h.Habits.GenerateWeekTasksAsync(Wednesday);

        // Simulate an interrupted run: one generated task was taken out of the sprint again.
        var first = (await h.Sprints.GetBoardAsync(Week)).ActiveTasks.First().Task.Id;
        await h.Sprints.RemoveTaskAsync(Week, first);

        var repair = await h.Habits.GenerateWeekTasksAsync(Wednesday);
        Assert.Equal((0, 1), (repair.Created, repair.AddedToSprint));
        Assert.Equal(2, (await h.Sprints.GetBoardAsync(Week)).ActiveTasks.Count());

        var next = await h.Habits.GenerateWeekTasksAsync(ServiceHarness.NextWeek);
        Assert.Equal(2, next.Created);
        Assert.Equal(2, (await h.Sprints.GetBoardAsync(ServiceHarness.NextWeek)).ActiveTasks.Count());
    }

    [Fact]
    public async Task Generation_is_refused_for_a_closed_sprint()
    {
        using var h = new ServiceHarness();
        await h.Habits.CreateAsync(Weekly());
        var task = await h.NewPlannableTaskAsync("Något");
        await h.Sprints.AddTaskAsync(Week, task);
        await h.Sprints.CloseAsync(Week, CarryDecision.Backlog);

        await AssertCodeAsync(PlanningException.Codes.SprintClosed, () => h.Habits.GenerateWeekTasksAsync(Wednesday));
    }

    [Fact]
    public async Task Finishing_a_generated_task_records_exactly_one_completion_even_when_toggled_repeatedly()
    {
        using var h = new ServiceHarness();
        var habit = await h.Habits.CreateAsync(Weekly());
        await h.Habits.GenerateWeekTasksAsync(Wednesday);
        var task = (await h.Sprints.GetBoardAsync(Week)).ActiveTasks.First().Task.Id;

        await h.Tasks.SetStatusAsync(task, TaskItemStatus.Done);
        Assert.Equal(1, (await h.Habits.GetAsync(habit))!.Habit.CountInPeriod);

        await h.Tasks.SetStatusAsync(task, TaskItemStatus.Done); // no-op
        await h.Tasks.SetStatusAsync(task, TaskItemStatus.InProgress);
        Assert.Equal(0, (await h.Habits.GetAsync(habit))!.Habit.CountInPeriod);

        await h.Tasks.SetStatusAsync(task, TaskItemStatus.Done);
        await h.Tasks.SetStatusAsync(task, TaskItemStatus.ToDo);
        await h.Tasks.SetStatusAsync(task, TaskItemStatus.Done);
        Assert.Equal(1, (await h.Habits.GetAsync(habit))!.Habit.CountInPeriod);
    }

    [Fact]
    public async Task Goal_contribution_stays_in_step_with_toggling_a_generated_task()
    {
        using var h = new ServiceHarness();
        var goal = await h.Goals.CreateAsync(new GoalInput("Pass", GoalHorizon.Year, new DateOnly(2026, 1, 1), GoalProgressMode.Numeric,
            StartValue: 0, TargetValue: 10));
        var habit = await h.Habits.CreateAsync(Weekly(goal: goal));
        await h.Habits.GenerateWeekTasksAsync(Wednesday);
        var task = (await h.Sprints.GetBoardAsync(Week)).ActiveTasks.First().Task.Id;

        await h.Tasks.SetStatusAsync(task, TaskItemStatus.Done);
        await h.Tasks.SetStatusAsync(task, TaskItemStatus.ToDo);
        await h.Tasks.SetStatusAsync(task, TaskItemStatus.Done);
        await h.Tasks.SetStatusAsync(task, TaskItemStatus.Done);

        Assert.Equal(1m, (await h.Goals.GetAsync(goal))!.Goal.CurrentValue);
        Assert.Equal(1, (await h.Habits.GetAsync(habit))!.Habit.CountInPeriod);
    }

    [Fact]
    public async Task Logging_a_habit_that_has_an_open_generated_task_finishes_that_task_instead_of_counting_twice()
    {
        using var h = new ServiceHarness();
        var habit = await h.Habits.CreateAsync(Weekly());
        await h.Habits.GenerateWeekTasksAsync(Wednesday);

        await h.Habits.LogAsync(habit);

        var detail = (await h.Habits.GetAsync(habit))!;
        Assert.Equal(1, detail.Habit.CountInPeriod);
        Assert.True(detail.Completions.Single().FromTask);
        var board = await h.Sprints.GetBoardAsync(Week);
        Assert.Equal(1, board.ActiveTasks.Count(t => t.Task.Status == TaskItemStatus.Done));
        Assert.Equal(2, board.ActiveTasks.Count(t => t.Task.Status == TaskItemStatus.ToDo));

        // Unlogging a task-based completion reopens the task.
        await h.Habits.UnlogAsync(habit, Wednesday);
        Assert.Equal(0, (await h.Habits.GetAsync(habit))!.Habit.CountInPeriod);
        Assert.Equal(3, (await h.Sprints.GetBoardAsync(Week)).ActiveTasks.Count(t => t.Task.Status == TaskItemStatus.ToDo));
    }

    [Fact]
    public async Task Generated_tasks_count_towards_a_task_driven_goal_without_double_counting()
    {
        using var h = new ServiceHarness();
        var goal = await h.Goals.CreateAsync(new GoalInput("Mål", GoalHorizon.Quarter, new DateOnly(2026, 10, 1), GoalProgressMode.LinkedTasks));
        await h.Habits.CreateAsync(Weekly(target: 2, goal: goal));
        await h.Habits.GenerateWeekTasksAsync(Wednesday);

        var detail = (await h.Goals.GetAsync(goal))!;
        Assert.Equal((0, 2), (detail.Goal.TasksDone, detail.Goal.TasksTotal));

        await h.Habits.LogAsync((await h.Habits.ListAsync())[0].Id); // finishes one generated task
        Assert.Equal(50m, (await h.Goals.GetAsync(goal))!.Goal.Percent);
    }

    [Fact]
    public async Task The_database_itself_refuses_duplicate_generated_tasks_and_duplicate_completions()
    {
        using var h = new ServiceHarness();
        var habit = await h.Habits.CreateAsync(Weekly());
        await h.Habits.GenerateWeekTasksAsync(Wednesday);
        var task = (await h.Sprints.GetBoardAsync(Week)).ActiveTasks.First().Task.Id;
        await h.Tasks.SetStatusAsync(task, TaskItemStatus.Done);

        await using var db = h.Db.CreateContext(h.User);
        db.Tasks.Add(new TaskItem { Title = "Dubblett", HabitId = habit, GeneratedForPeriodStart = Week, GeneratedIndex = 1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var db2 = h.Db.CreateContext(h.User);
        db2.HabitCompletions.Add(new HabitCompletion { HabitId = habit, Date = Wednesday, CompletedUtc = h.Clock.Now, TaskItemId = task });
        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Archived_habits_are_hidden_from_the_default_list_and_isolated_per_owner()
    {
        using var h = new ServiceHarness(userId: "user-a");
        var id = await h.Habits.CreateAsync(Weekly());
        await h.Habits.SetArchivedAsync(id, true);
        Assert.Empty(await h.Habits.ListAsync());
        Assert.Single(await h.Habits.ListAsync(includeArchived: true));

        await h.Habits.SetArchivedAsync(id, false);
        var other = h.ForUser("user-b");
        Assert.Empty(await other.Habits.ListAsync(includeArchived: true));
        Assert.Null(await other.Habits.GetAsync(id));
        await AssertCodeAsync(PlanningException.Codes.NotFound, () => other.Habits.LogAsync(id));
        await AssertCodeAsync(PlanningException.Codes.NotFound, () => other.Habits.SetArchivedAsync(id, true));
    }

    [Fact]
    public async Task Backlog_is_ordered_by_priority_severity_not_alphabetically()
    {
        using var h = new ServiceHarness();
        await h.Tasks.CreateAsync(new TaskInput("Ingen"));
        await h.Tasks.CreateAsync(new TaskInput("Låg", Priority: TaskPriority.Low));
        await h.Tasks.CreateAsync(new TaskInput("Hög", Priority: TaskPriority.High));
        await h.Tasks.CreateAsync(new TaskInput("Medel", Priority: TaskPriority.Medium));

        Assert.Equal(["Hög", "Medel", "Låg", "Ingen"], (await h.Tasks.ListBacklogAsync()).Select(t => t.Title));
    }

    [Fact]
    public async Task Ensuring_the_week_adds_tasks_for_weekly_items_only_and_never_duplicates()
    {
        using var h = new ServiceHarness();
        await h.Habits.CreateAsync(Weekly(target: 2));
        await h.Habits.CreateAsync(new HabitInput("Stretcha", HabitPeriod.Day, 1));

        await h.Habits.EnsureWeekTasksAsync(Wednesday);
        await h.Habits.EnsureWeekTasksAsync(Wednesday);

        var board = await h.Sprints.GetBoardAsync(Week);
        Assert.Equal(["Cykla (1/2)", "Cykla (2/2)"], board.ActiveTasks.Select(t => t.Task.Title));
        Assert.All(board.ActiveTasks, t => Assert.NotNull(t.Task.HabitId));
    }

    [Fact]
    public async Task Ensuring_a_closed_week_quietly_does_nothing()
    {
        using var h = new ServiceHarness();
        await h.Habits.CreateAsync(Weekly());
        var task = await h.NewPlannableTaskAsync("Något");
        await h.Sprints.AddTaskAsync(Week, task);
        await h.Sprints.CloseAsync(Week, CarryDecision.Backlog);

        await h.Habits.EnsureWeekTasksAsync(Wednesday);

        Assert.Empty((await h.Sprints.GetBoardAsync(Week)).ActiveTasks);
    }

    [Fact]
    public async Task The_backlog_can_be_filtered_to_recurring_tasks()
    {
        using var h = new ServiceHarness();
        await h.Habits.CreateAsync(Weekly(target: 1));
        await h.NewPlannableTaskAsync("Vanlig uppgift");
        await h.Habits.EnsureWeekTasksAsync(Wednesday);

        var recurring = await h.Tasks.ListBacklogAsync(new LifeOS.Application.Organize.TaskFilter(RecurringOnly: true));

        Assert.Equal(["Cykla (1/1)"], recurring.Select(t => t.Title));
        Assert.Equal(2, (await h.Tasks.ListBacklogAsync()).Count);
    }
}
