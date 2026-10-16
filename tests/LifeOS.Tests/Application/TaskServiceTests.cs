using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Domain.Projects;
using LifeOS.Domain.Tasks;
using LifeOS.Tests.Infrastructure;

namespace LifeOS.Tests.Application;

public class TaskServiceTests
{
    private static async Task AssertCodeAsync(string code, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<PlanningException>(action);
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public async Task Quick_capture_needs_only_a_title_and_lands_in_the_inbox()
    {
        using var h = new ServiceHarness();
        var id = await h.Tasks.CaptureAsync("  Ring tandläkaren  ");

        var task = (await h.Tasks.GetAsync(id))!.Task;
        Assert.Equal("Ring tandläkaren", task.Title);
        Assert.Equal(TaskTriage.Inbox, task.Triage);
        Assert.Equal(TaskItemStatus.ToDo, task.Status);
        Assert.Null(task.LifeAreaId);
        Assert.Null(task.ProjectId);
        Assert.Equal(1, await h.Tasks.InboxCountAsync());
        Assert.Single(await h.Tasks.ListInboxAsync());
    }

    [Fact]
    public async Task Blank_titles_are_rejected_and_long_titles_are_trimmed()
    {
        using var h = new ServiceHarness();
        await AssertCodeAsync(PlanningException.Codes.TitleRequired, () => h.Tasks.CaptureAsync("   "));

        var id = await h.Tasks.CaptureAsync(new string('x', 500));
        Assert.Equal(TaskService.MaxTitleLength, (await h.Tasks.GetAsync(id))!.Task.Title.Length);
    }

    [Fact]
    public async Task Triage_moves_a_task_from_inbox_to_backlog()
    {
        using var h = new ServiceHarness();
        var id = await h.Tasks.CaptureAsync("Sortera mig");
        await h.Tasks.SetTriageAsync(id, TaskTriage.Backlog);

        Assert.Empty(await h.Tasks.ListInboxAsync());
        Assert.Equal(id, Assert.Single(await h.Tasks.ListBacklogAsync()).Id);
    }

    [Fact]
    public async Task Tasks_do_not_need_a_project_or_goal_but_can_have_area_project_and_dates()
    {
        using var h = new ServiceHarness();
        var area = await h.Areas.CreateAsync("Cykling");
        var project = await h.Projects.CreateAsync(new ProjectInput("Sommarlopp", LifeAreaId: area));
        var id = await h.Tasks.CreateAsync(new TaskInput(
            "Långpass",
            Notes: "Zon 2",
            Priority: TaskPriority.High,
            EstimateMinutes: 180,
            DueDate: new DateOnly(2026, 10, 20),
            ScheduledDate: new DateOnly(2026, 10, 18),
            ScheduledTime: new TimeOnly(9, 30),
            LifeAreaId: area,
            ProjectId: project));

        var task = (await h.Tasks.GetAsync(id))!.Task;
        Assert.Equal("Cykling", task.LifeAreaName);
        Assert.Equal("Sommarlopp", task.ProjectName);
        Assert.Equal(TaskPriority.High, task.Priority);
        Assert.Equal(new TimeOnly(9, 30), task.ScheduledTime);
        Assert.Equal(TaskTriage.Backlog, task.Triage);
    }

    [Fact]
    public async Task A_time_without_a_date_is_ignored_and_invalid_estimates_are_rejected()
    {
        using var h = new ServiceHarness();
        var id = await h.Tasks.CreateAsync(new TaskInput("Utan dag", ScheduledTime: new TimeOnly(8, 0)));
        Assert.Null((await h.Tasks.GetAsync(id))!.Task.ScheduledTime);

        await AssertCodeAsync(PlanningException.Codes.InvalidEstimate,
            () => h.Tasks.CreateAsync(new TaskInput("Fel", EstimateMinutes: 0)));
        await AssertCodeAsync(PlanningException.Codes.InvalidEstimate,
            () => h.Tasks.UpdateAsync(id, new TaskInput("Fel", EstimateMinutes: -5)));
    }

    [Fact]
    public async Task Subtasks_allow_one_level_and_inherit_area_and_project()
    {
        using var h = new ServiceHarness();
        var area = await h.Areas.CreateAsync("Hem");
        var parent = await h.Tasks.CreateAsync(new TaskInput("Renovera", LifeAreaId: area));
        var sub = await h.Tasks.AddSubtaskAsync(parent, "Måla väggar");

        await AssertCodeAsync(PlanningException.Codes.SubtaskTooDeep, () => h.Tasks.AddSubtaskAsync(sub, "För djupt"));

        var detail = (await h.Tasks.GetAsync(parent))!;
        var child = Assert.Single(detail.Subtasks);
        Assert.Equal(area, child.LifeAreaId);
        Assert.Equal(1, detail.Task.SubtaskTotal);
        Assert.Equal(0, detail.Task.SubtaskDone);

        await h.Tasks.SetStatusAsync(sub, TaskItemStatus.Done);
        Assert.Equal(1, (await h.Tasks.GetAsync(parent))!.Task.SubtaskDone);

        // Subtasks never show up as top-level backlog or inbox items.
        Assert.Equal(parent, Assert.Single(await h.Tasks.ListBacklogAsync()).Id);
    }

    [Fact]
    public async Task Completion_time_is_set_when_done_and_cleared_when_reopened()
    {
        using var h = new ServiceHarness();
        var id = await h.NewPlannableTaskAsync("Gör klart");

        await h.Tasks.SetStatusAsync(id, TaskItemStatus.Done);
        Assert.Equal(h.Clock.Now, (await h.Tasks.GetAsync(id))!.Task.CompletedUtc);

        await h.Tasks.SetStatusAsync(id, TaskItemStatus.InProgress);
        Assert.Null((await h.Tasks.GetAsync(id))!.Task.CompletedUtc);
    }

    [Fact]
    public async Task Backlog_lists_only_open_top_level_backlog_work_and_supports_filters()
    {
        using var h = new ServiceHarness();
        var area = await h.Areas.CreateAsync("Hälsa");
        var other = await h.Areas.CreateAsync("Hem");
        var project = await h.Projects.CreateAsync(new ProjectInput("Löpning"));
        var run = await h.Tasks.CreateAsync(new TaskInput("Springa 5 km", LifeAreaId: area, ProjectId: project));
        await h.Tasks.CreateAsync(new TaskInput("Städa köket", LifeAreaId: other));
        var done = await h.NewPlannableTaskAsync("Redan klar");
        await h.Tasks.SetStatusAsync(done, TaskItemStatus.Done);
        await h.Tasks.CaptureAsync("Ligger i inkorgen");

        Assert.Equal(2, (await h.Tasks.ListBacklogAsync()).Count);
        Assert.Equal(run, Assert.Single(await h.Tasks.ListBacklogAsync(new TaskFilter(LifeAreaId: area))).Id);
        Assert.Equal(run, Assert.Single(await h.Tasks.ListBacklogAsync(new TaskFilter(ProjectId: project))).Id);
        Assert.Equal(run, Assert.Single(await h.Tasks.ListBacklogAsync(new TaskFilter(Text: "spring"))).Id);
    }

    [Fact]
    public async Task Plannable_excludes_tasks_already_planned_in_an_open_sprint()
    {
        using var h = new ServiceHarness();
        var planned = await h.NewPlannableTaskAsync("Planerad");
        var free = await h.NewPlannableTaskAsync("Ledig");
        await h.Sprints.AddTaskAsync(ServiceHarness.Week, planned);

        Assert.Equal(2, (await h.Tasks.ListBacklogAsync()).Count);
        Assert.Equal(free, Assert.Single(await h.Tasks.ListPlannableAsync()).Id);
    }

    [Fact]
    public async Task Deleting_an_unplanned_task_removes_it_and_its_subtasks()
    {
        using var h = new ServiceHarness();
        var parent = await h.NewPlannableTaskAsync("Förälder");
        await h.Tasks.AddSubtaskAsync(parent, "Barn");

        Assert.Equal(DeleteOutcome.Deleted, await h.Tasks.DeleteAsync(parent));
        Assert.Null(await h.Tasks.GetAsync(parent));
        Assert.Empty(await h.Tasks.ListBacklogAsync());
    }

    [Fact]
    public async Task Deleting_a_task_with_sprint_history_cancels_it_so_history_stays_intact()
    {
        using var h = new ServiceHarness();
        var id = await h.NewPlannableTaskAsync("Har historik");
        await h.Sprints.AddTaskAsync(ServiceHarness.Week, id);
        await h.Tasks.SetFocusAsync(id, new DateOnly(2026, 10, 14));

        Assert.Equal(DeleteOutcome.Cancelled, await h.Tasks.DeleteAsync(id));

        var task = (await h.Tasks.GetAsync(id))!.Task;
        Assert.Equal(TaskItemStatus.Cancelled, task.Status);
        Assert.Null(task.FocusDate);
        Assert.Empty(await h.Tasks.ListBacklogAsync());
        Assert.Single(await h.Sprints.GetScopeLogAsync(ServiceHarness.Week));
    }

    [Fact]
    public async Task Missing_tasks_report_not_found()
    {
        using var h = new ServiceHarness();
        await AssertCodeAsync(PlanningException.Codes.NotFound, () => h.Tasks.SetStatusAsync(Guid.NewGuid(), TaskItemStatus.Done));
        Assert.Null(await h.Tasks.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Projects_count_open_and_done_tasks_and_hide_closed_projects_by_default()
    {
        using var h = new ServiceHarness();
        var project = await h.Projects.CreateAsync(new ProjectInput("  Köksrenovering  ", "Beskrivning"));
        var a = await h.Tasks.CreateAsync(new TaskInput("A", ProjectId: project));
        await h.Tasks.CreateAsync(new TaskInput("B", ProjectId: project));
        await h.Tasks.SetStatusAsync(a, TaskItemStatus.Done);

        var view = (await h.Projects.GetAsync(project))!;
        Assert.Equal("Köksrenovering", view.Name);
        Assert.Equal(1, view.OpenTasks);
        Assert.Equal(1, view.DoneTasks);

        await h.Projects.SetStatusAsync(project, ProjectStatus.Completed);
        Assert.Empty(await h.Projects.ListAsync());
        Assert.Single(await h.Projects.ListAsync(includeClosed: true));
        await AssertCodeAsync(PlanningException.Codes.NameRequired, () => h.Projects.CreateAsync(new ProjectInput(" ")));
    }

    [Fact]
    public async Task Life_areas_can_be_seeded_from_suggestions_without_duplicates_and_archived()
    {
        using var h = new ServiceHarness();
        await h.Areas.CreateAsync("hem");
        await h.Areas.CreateManyAsync(["Hem", "Hälsa", " Hälsa ", ""]);

        var areas = await h.Areas.ListAsync();
        Assert.Equal(["hem", "Hälsa"], areas.Select(a => a.Name));
        Assert.DoesNotContain(areas, a => a.Name.Contains("golf", StringComparison.OrdinalIgnoreCase));

        await h.Areas.SetArchivedAsync(areas[0].Id, true);
        Assert.Single(await h.Areas.ListAsync());
        Assert.Equal(2, (await h.Areas.ListAsync(includeArchived: true)).Count);
    }
}
