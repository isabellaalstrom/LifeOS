using LifeOS.Application.Organize;
using LifeOS.Domain.Common;
using LifeOS.Domain.Tasks;
using LifeOS.Tests.Infrastructure;

namespace LifeOS.Tests.Application;

public class TodayAndIsolationTests
{
    [Fact]
    public async Task Today_uses_the_users_local_date_for_focus_and_schedule()
    {
        // 22:30 UTC on the 14th is 00:30 on the 15th in Stockholm.
        using var h = new ServiceHarness(now: "2026-10-14T22:30:00Z");
        Assert.Equal(new DateOnly(2026, 10, 15), h.Clock.Today);

        var focus = await h.NewPlannableTaskAsync("Fokus");
        var scheduled = await h.Tasks.CreateAsync(new TaskInput("Schemalagd", ScheduledDate: new DateOnly(2026, 10, 15), ScheduledTime: new TimeOnly(8, 0)));
        var yesterday = await h.NewPlannableTaskAsync("Gårdagens fokus");
        await h.Tasks.SetFocusAsync(focus, new DateOnly(2026, 10, 15));
        await h.Tasks.SetFocusAsync(yesterday, new DateOnly(2026, 10, 14));

        var today = await h.Today.GetTodayAsync();
        Assert.Equal(new DateOnly(2026, 10, 15), today.Date);
        Assert.Equal(["Schemalagd", "Fokus"], today.Focus.Select(t => t.Title)); // scheduled time first, then the rest
        Assert.DoesNotContain(today.Focus, t => t.Id == yesterday);
        Assert.Contains(today.Focus, t => t.Id == scheduled);
    }

    [Fact]
    public async Task Overdue_and_due_today_are_listed_once_and_finished_or_cancelled_work_is_left_out()
    {
        using var h = new ServiceHarness();
        var overdue = await h.Tasks.CreateAsync(new TaskInput("Försenad", DueDate: new DateOnly(2026, 10, 10)));
        var dueToday = await h.Tasks.CreateAsync(new TaskInput("Idag", DueDate: new DateOnly(2026, 10, 14)));
        var later = await h.Tasks.CreateAsync(new TaskInput("Senare", DueDate: new DateOnly(2026, 10, 30)));
        var finished = await h.Tasks.CreateAsync(new TaskInput("Klar", DueDate: new DateOnly(2026, 10, 9)));
        await h.Tasks.SetStatusAsync(finished, TaskItemStatus.Done);
        var inFocus = await h.Tasks.CreateAsync(new TaskInput("I fokus", DueDate: new DateOnly(2026, 10, 14)));
        await h.Tasks.SetFocusAsync(inFocus, new DateOnly(2026, 10, 14));

        var today = await h.Today.GetTodayAsync();
        Assert.Equal(["Försenad", "Idag"], today.DueOrOverdue.Select(t => t.Title));
        Assert.Equal(["I fokus"], today.Focus.Select(t => t.Title));
        Assert.DoesNotContain(today.DueOrOverdue, t => t.Id == later || t.Id == finished);
        Assert.Contains(today.DueOrOverdue, t => t.Id == overdue);
        Assert.Contains(today.DueOrOverdue, t => t.Id == dueToday);
    }

    [Fact]
    public async Task Sprint_tasks_not_yet_in_focus_are_suggested_in_sprint_order()
    {
        using var h = new ServiceHarness();
        var a = await h.NewPlannableTaskAsync("A");
        var b = await h.NewPlannableTaskAsync("B");
        var c = await h.NewPlannableTaskAsync("C");
        foreach (var id in new[] { a, b, c })
        {
            await h.Sprints.AddTaskAsync(ServiceHarness.Week, id);
        }

        await h.Tasks.SetFocusAsync(b, h.Clock.Today);

        var today = await h.Today.GetTodayAsync();
        Assert.Equal(["B"], today.Focus.Select(t => t.Title));
        Assert.Equal(["A", "C"], today.SprintSuggestions.Select(t => t.Title));
    }

    [Fact]
    public async Task Overview_summarizes_sprint_deadlines_inbox_and_forgotten_sprints()
    {
        using var h = new ServiceHarness(now: "2026-10-13T12:00:00Z");
        var todo = await h.NewPlannableTaskAsync("Att göra", 60);
        var doing = await h.NewPlannableTaskAsync("Pågår", 30);
        var done = await h.NewPlannableTaskAsync("Klar", 30);
        foreach (var id in new[] { todo, doing, done })
        {
            await h.Sprints.AddTaskAsync(ServiceHarness.Week, id);
        }

        await h.Tasks.SetStatusAsync(doing, TaskItemStatus.InProgress);
        await h.Tasks.SetStatusAsync(done, TaskItemStatus.Done);
        await h.Sprints.SetGoalAsync(ServiceHarness.Week, "Bra vecka");
        await h.Sprints.SetCapacityAsync(ServiceHarness.Week, 100);
        await h.Tasks.CaptureAsync("Inkorgen");
        await h.Tasks.CreateAsync(new TaskInput("Deadline snart", DueDate: new DateOnly(2026, 10, 20)));
        await h.Tasks.CreateAsync(new TaskInput("Deadline långt bort", DueDate: new DateOnly(2027, 1, 1)));

        var overview = await h.Today.GetOverviewAsync();
        Assert.Equal("Bra vecka", overview.Sprint.Goal);
        Assert.Equal((1, 1, 1), (overview.Sprint.ToDo, overview.Sprint.InProgress, overview.Sprint.Done));
        Assert.Equal(120, overview.Sprint.PlannedMinutes);
        Assert.Equal(LifeOS.Domain.Planning.CapacityStatus.Over, overview.Sprint.Capacity);
        Assert.Equal(1, overview.InboxCount);
        Assert.Equal(["Deadline snart"], overview.Deadlines.Select(t => t.Title));
        Assert.Empty(overview.PastSprintsToClose);

        h.Clock.Set("2026-10-21T09:00:00Z");
        var later = await h.Today.GetOverviewAsync();
        Assert.False(later.Sprint.Exists);
        Assert.Equal(ServiceHarness.Week, Assert.Single(later.PastSprintsToClose).WeekStart);
    }

    [Fact]
    public async Task One_owner_never_sees_or_changes_another_owners_planning_data()
    {
        using var h = new ServiceHarness(userId: "user-a");
        var task = await h.NewPlannableTaskAsync("Privat");
        await h.Areas.CreateAsync("Mitt område");
        await h.Sprints.AddTaskAsync(ServiceHarness.Week, task);

        var other = h.ForUser("user-b");
        Assert.Empty(await other.Tasks.ListBacklogAsync());
        Assert.Empty(await other.Areas.ListAsync());
        Assert.Null(await other.Tasks.GetAsync(task));
        Assert.Empty((await other.Sprints.GetBoardAsync(ServiceHarness.Week)).Tasks);
        Assert.Null((await other.Sprints.GetBoardAsync(ServiceHarness.Week)).SprintId);

        await Assert.ThrowsAsync<LifeOS.Application.Planning.PlanningException>(
            () => other.Tasks.SetStatusAsync(task, TaskItemStatus.Done));
        await Assert.ThrowsAsync<LifeOS.Application.Planning.PlanningException>(
            () => other.Sprints.AddTaskAsync(ServiceHarness.Week, task));

        // Both owners can have their own sprint for the same week.
        var theirs = await other.Tasks.CreateAsync(new TaskInput("Deras"));
        await other.Sprints.AddTaskAsync(ServiceHarness.Week, theirs);
        Assert.Single((await other.Sprints.GetBoardAsync(ServiceHarness.Week)).Tasks);
        Assert.Single((await h.Sprints.GetBoardAsync(ServiceHarness.Week)).Tasks);
    }

    [Fact]
    public void Every_owned_entity_type_has_an_owner_query_filter()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext(new FakeCurrentUser("user-a"));

        var owned = typeof(OwnedEntity).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(OwnedEntity).IsAssignableFrom(t))
            .ToList();
        Assert.NotEmpty(owned);

        foreach (var type in owned)
        {
            var entity = context.Model.FindEntityType(type);
            Assert.True(entity is not null, $"{type.Name} is not mapped");
            Assert.True(entity!.GetDeclaredQueryFilters().Any(), $"{type.Name} has no owner query filter");
        }
    }
}
