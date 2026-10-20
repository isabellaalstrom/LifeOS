using LifeOS.Application.Goals;
using LifeOS.Application.Habits;
using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Application.Retros;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Habits;
using LifeOS.Domain.Retros;
using LifeOS.Domain.Tasks;
using LifeOS.Tests.Infrastructure;

namespace LifeOS.Tests.Application;

public class RetroServiceTests
{
    private static readonly DateOnly Oct = new(2026, 10, 1);

    private static async Task AssertCodeAsync(string code, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<PlanningException>(action);
        Assert.Equal(code, ex.Code);
    }

    private static readonly WellbeingInput NoWellbeing = new(null, null, null, null);

    // ---------- periods ----------

    [Theory]
    [InlineData(RetroType.Weekly, "2026-10-18", "2026-10-12", "2026-10-19")]
    [InlineData(RetroType.Weekly, "2027-01-01", "2026-12-28", "2027-01-04")]   // week across a year boundary
    [InlineData(RetroType.Monthly, "2026-02-28", "2026-02-01", "2026-03-01")]
    [InlineData(RetroType.Monthly, "2026-12-31", "2026-12-01", "2027-01-01")]
    [InlineData(RetroType.Quarterly, "2026-09-30", "2026-07-01", "2026-10-01")]
    [InlineData(RetroType.Quarterly, "2026-10-01", "2026-10-01", "2027-01-01")]
    [InlineData(RetroType.Annual, "2026-06-15", "2026-01-01", "2027-01-01")]
    public void Retro_periods_are_calendar_periods(RetroType type, string date, string start, string end)
    {
        var s = RetroPeriods.Start(type, DateOnly.Parse(date));
        Assert.Equal(DateOnly.Parse(start), s);
        Assert.Equal(DateOnly.Parse(end), RetroPeriods.EndExclusive(type, s));
    }

    [Fact]
    public void Smaller_types_nest_inside_larger_ones()
    {
        Assert.Equal(RetroType.Quarterly, RetroPeriods.SmallerType(RetroType.Annual));
        Assert.Equal(RetroType.Monthly, RetroPeriods.SmallerType(RetroType.Quarterly));
        Assert.Equal(RetroType.Weekly, RetroPeriods.SmallerType(RetroType.Monthly));
        Assert.Null(RetroPeriods.SmallerType(RetroType.Weekly));
        Assert.Equal(new DateOnly(2026, 9, 1), RetroPeriods.Previous(RetroType.Monthly, Oct));
        Assert.Equal(new DateOnly(2025, 1, 1), RetroPeriods.Previous(RetroType.Annual, new DateOnly(2026, 1, 1)));
    }

    // ---------- drafts ----------

    [Fact]
    public async Task A_retro_can_be_started_any_time_resumed_and_is_unique_per_period()
    {
        using var h = new ServiceHarness(); // 14 Oct 2026, mid-month
        var first = await h.Retros.StartOrResumeAsync(RetroType.Monthly, new DateOnly(2026, 10, 14));
        var again = await h.Retros.StartOrResumeAsync(RetroType.Monthly, new DateOnly(2026, 10, 3));
        var weekly = await h.Retros.StartOrResumeAsync(RetroType.Weekly, new DateOnly(2026, 10, 14));

        Assert.Equal(first, again);
        Assert.NotEqual(first, weekly);
        var detail = (await h.Retros.GetAsync(first))!;
        Assert.Equal(RetroStatus.Draft, detail.Retro.Status);
        Assert.Equal(Oct, detail.Retro.PeriodStart);
        Assert.Equal(new DateOnly(2026, 11, 1), detail.Retro.PeriodEndExclusive);
        Assert.Equal(2, (await h.Retros.ListAsync()).Count);
        Assert.Single(await h.Retros.ListAsync(RetroType.Weekly));
    }

    [Fact]
    public async Task Answers_and_wellbeing_are_saved_as_a_draft_and_blank_answers_are_removed()
    {
        using var h = new ServiceHarness();
        var id = await h.Retros.StartOrResumeAsync(RetroType.Monthly, Oct);

        await h.Retros.SaveAsync(id,
            new Dictionary<string, string?> { ["highlights"] = "  Första långpasset  ", ["challenges"] = "Trött", ["learned"] = null },
            "Bra månad överlag", new WellbeingInput(4, null, 2, 5));

        var detail = (await h.Retros.GetAsync(id))!;
        Assert.Equal("Första långpasset", detail.Answers["highlights"]);
        Assert.Equal(2, detail.Answers.Count);
        Assert.Equal("Bra månad överlag", detail.Retro.Summary);
        Assert.Equal((4, (int?)null, 2, 5), (detail.Retro.Energy!.Value, detail.Retro.Balance, detail.Retro.Stress!.Value, detail.Retro.Satisfaction!.Value));

        // Clearing an answer removes it; well-being stays optional.
        await h.Retros.SaveAsync(id, new Dictionary<string, string?> { ["highlights"] = "" }, null, NoWellbeing);
        detail = (await h.Retros.GetAsync(id))!;
        Assert.DoesNotContain("highlights", detail.Answers.Keys);
        Assert.Null(detail.Retro.Energy);
    }

    [Fact]
    public async Task Invalid_wellbeing_and_unknown_questions_are_rejected()
    {
        using var h = new ServiceHarness();
        var id = await h.Retros.StartOrResumeAsync(RetroType.Monthly, Oct);

        await AssertCodeAsync(PlanningException.Codes.InvalidWellbeing,
            () => h.Retros.SaveAsync(id, new Dictionary<string, string?>(), null, new WellbeingInput(6, null, null, null)));
        await AssertCodeAsync(PlanningException.Codes.InvalidWellbeing,
            () => h.Retros.SaveAsync(id, new Dictionary<string, string?>(), null, new WellbeingInput(null, 0, null, null)));
        await AssertCodeAsync(PlanningException.Codes.InvalidQuestion,
            () => h.Retros.SaveAsync(id, new Dictionary<string, string?> { ["year_story"] = "fel typ" }, null, NoWellbeing));
    }

    [Fact]
    public async Task A_retro_completes_without_any_answers_and_cannot_be_edited_until_reopened()
    {
        using var h = new ServiceHarness();
        var id = await h.Retros.StartOrResumeAsync(RetroType.Weekly, h.Clock.Today);

        await h.Retros.CompleteAsync(id);
        var done = (await h.Retros.GetAsync(id))!;
        Assert.Equal(RetroStatus.Completed, done.Retro.Status);
        Assert.NotNull(done.Retro.CompletedUtc);
        Assert.True(done.StatsAreSnapshot);

        await AssertCodeAsync(PlanningException.Codes.RetroCompleted,
            () => h.Retros.SaveAsync(id, new Dictionary<string, string?>(), "x", NoWellbeing));
        await AssertCodeAsync(PlanningException.Codes.RetroCompleted, () => h.Retros.DeleteDraftAsync(id));

        await h.Retros.ReopenAsync(id);
        await h.Retros.SaveAsync(id, new Dictionary<string, string?>(), "ändrad", NoWellbeing);
        Assert.Equal(RetroStatus.Draft, (await h.Retros.GetAsync(id))!.Retro.Status);
    }

    [Fact]
    public async Task Deleting_a_draft_removes_it_but_keeps_the_actions_decided_in_it()
    {
        using var h = new ServiceHarness();
        var id = await h.Retros.StartOrResumeAsync(RetroType.Monthly, Oct);
        await h.Retros.SaveAsync(id, new Dictionary<string, string?> { ["highlights"] = "x" }, null, NoWellbeing);
        await h.Retros.AddActionAsync(id, new RetroActionInput("Boka cykelservice"));

        await h.Retros.DeleteDraftAsync(id);

        Assert.Null(await h.Retros.GetAsync(id));
        Assert.Single(await h.Retros.ListOpenActionsAsync());
    }

    // ---------- statistics ----------

    [Fact]
    public async Task Tasks_completed_are_counted_in_the_owners_local_period_not_utc()
    {
        using var h = new ServiceHarness(now: "2026-09-30T21:00:00Z"); // 23:00 on 30 Sep in Stockholm
        var lastEvening = await h.Tasks.CreateAsync(new TaskInput("Sista kvällen", EstimateMinutes: 30));
        await h.Tasks.SetStatusAsync(lastEvening, TaskItemStatus.Done);

        h.Clock.Set("2026-09-30T22:30:00Z"); // 00:30 on 1 Oct locally
        var firstNight = await h.Tasks.CreateAsync(new TaskInput("Första natten", EstimateMinutes: 45));
        await h.Tasks.SetStatusAsync(firstNight, TaskItemStatus.Done);

        h.Clock.Set("2026-10-20T10:00:00Z");
        var sept = await h.RetroStats.ComputeAsync(RetroType.Monthly, new DateOnly(2026, 9, 15));
        var oct = await h.RetroStats.ComputeAsync(RetroType.Monthly, Oct);

        Assert.Equal((1, 30), (sept.TasksCompleted, sept.TasksCompletedMinutes));
        Assert.Equal((1, 45), (oct.TasksCompleted, oct.TasksCompletedMinutes));
        Assert.False(sept.InProgress);
        Assert.True(oct.InProgress);
    }

    [Fact]
    public async Task Subtasks_cancelled_and_open_tasks_do_not_count_as_completed()
    {
        using var h = new ServiceHarness();
        var parent = await h.NewPlannableTaskAsync("Förälder");
        var sub = await h.Tasks.AddSubtaskAsync(parent, "Del");
        var cancelled = await h.NewPlannableTaskAsync("Avbruten");
        await h.NewPlannableTaskAsync("Öppen");
        await h.Tasks.SetStatusAsync(sub, TaskItemStatus.Done);
        await h.Tasks.SetStatusAsync(cancelled, TaskItemStatus.Cancelled);
        await h.Tasks.SetStatusAsync(parent, TaskItemStatus.Done);

        var stats = await h.RetroStats.ComputeAsync(RetroType.Monthly, Oct);
        Assert.Equal(1, stats.TasksCompleted);
    }

    [Fact]
    public async Task Sprint_scope_is_summed_over_the_weeks_of_the_period_and_a_week_belongs_to_the_month_of_its_monday()
    {
        using var h = new ServiceHarness(now: "2026-10-04T12:00:00Z"); // Sunday before Monday 5 Oct
        var a = await h.NewPlannableTaskAsync("A");
        var b = await h.NewPlannableTaskAsync("B");
        await h.Sprints.AddTaskAsync(new DateOnly(2026, 10, 5), a);
        await h.Sprints.AddTaskAsync(new DateOnly(2026, 10, 5), b);

        h.Clock.Set("2026-10-07T10:00:00Z");
        var c = await h.NewPlannableTaskAsync("C");
        await h.Sprints.AddTaskAsync(new DateOnly(2026, 10, 5), c); // added after the week started
        await h.Tasks.SetStatusAsync(a, TaskItemStatus.Done);

        h.Clock.Set("2026-10-11T18:00:00Z");
        await h.Sprints.CloseAsync(new DateOnly(2026, 10, 5), CarryDecision.NextSprint); // b and c carried over

        // A week starting on Monday 28 Sep belongs to September even though it ends in October.
        var lateSept = await h.NewPlannableTaskAsync("Sen september");
        await h.Sprints.AddTaskAsync(new DateOnly(2026, 9, 28), lateSept);

        h.Clock.Set("2026-11-02T10:00:00Z");
        var oct = (await h.RetroStats.ComputeAsync(RetroType.Monthly, Oct)).Sprints;
        var sept = (await h.RetroStats.ComputeAsync(RetroType.Monthly, new DateOnly(2026, 9, 1))).Sprints;

        Assert.Equal(2, oct.SprintCount);        // weeks of 5 Oct and 12 Oct
        Assert.Equal(2, oct.Planned);            // a, b were planned before week 41 started
        Assert.Equal(1, oct.AddedAfterStart);    // c arrived during the week
        Assert.Equal(2, oct.CarriedOut);         // b and c moved on
        Assert.Equal(2, oct.CarriedIn);          // ... and arrived in the next week
        Assert.Equal(1, oct.Completed);          // a
        Assert.Equal(1, sept.SprintCount);
        Assert.Equal(0, sept.Planned);           // the late-September task was planned after that week began
    }

    [Fact]
    public async Task Goal_changes_use_history_and_goals_without_activity_are_left_out()
    {
        using var h = new ServiceHarness(now: "2026-09-10T10:00:00Z");
        var active = await h.Goals.CreateAsync(new GoalInput("FTP", GoalHorizon.Year, new DateOnly(2026, 1, 1), GoalProgressMode.Numeric,
            StartValue: 200, TargetValue: 300, Unit: "W"));
        var idle = await h.Goals.CreateAsync(new GoalInput("Vilande", GoalHorizon.Year, new DateOnly(2026, 1, 1), GoalProgressMode.Manual));
        await h.Goals.SetNumericValueAsync(active, 220); // 20% in September

        h.Clock.Set("2026-10-10T10:00:00Z");
        await h.Goals.SetNumericValueAsync(active, 250); // 50% in October
        h.Clock.Set("2026-10-25T10:00:00Z");
        await h.Goals.SetNumericValueAsync(active, 270); // 70%
        await h.Goals.SetStatusAsync(active, GoalStatus.Achieved);

        h.Clock.Set("2026-11-03T10:00:00Z");
        var oct = await h.RetroStats.ComputeAsync(RetroType.Monthly, Oct);
        var change = Assert.Single(oct.Goals);
        Assert.Equal("FTP", change.Title);
        Assert.Equal(20m, change.StartPercent);
        Assert.Equal(70m, change.EndPercent);
        Assert.True(change.AchievedInPeriod);

        // October is the first period in which a quarterly/annual view shows nothing for the idle goal: it is simply absent.
        Assert.DoesNotContain(oct.Goals, g => g.GoalId == idle);
        var earlier = await h.RetroStats.ComputeAsync(RetroType.Monthly, new DateOnly(2026, 8, 1));
        Assert.Empty(earlier.Goals);
    }

    [Fact]
    public async Task Habit_consistency_counts_only_whole_elapsed_periods_after_the_habit_existed()
    {
        using var h = new ServiceHarness(now: "2026-10-01T10:00:00Z"); // habit exists from Thursday 1 Oct
        var habit = await h.Habits.CreateAsync(new HabitInput("Cykla", HabitPeriod.Week, 2));
        foreach (var d in new[] { "2026-10-05", "2026-10-06", "2026-10-12", "2026-10-19", "2026-10-20" })
        {
            h.Clock.Set(d + "T10:00:00Z");
            await h.Habits.LogAsync(habit);
        }

        h.Clock.Set("2026-10-31T10:00:00Z");
        var stat = Assert.Single((await h.RetroStats.ComputeAsync(RetroType.Monthly, Oct)).Habits);

        // Whole weeks inside October that began after creation: 5, 12, 19 Oct (26 Oct ends on 2 Nov, outside the month).
        Assert.Equal(3, stat.PeriodsTotal);
        Assert.Equal(2, stat.PeriodsMet); // week 5 (2/2) and week 19 (2/2); week 12 had only 1
        Assert.Equal(5, stat.Completions);
    }

    [Fact]
    public async Task A_period_in_progress_only_counts_periods_that_have_fully_passed()
    {
        using var h = new ServiceHarness(now: "2026-10-01T10:00:00Z");
        await h.Habits.CreateAsync(new HabitInput("Stretcha", HabitPeriod.Week, 3));

        h.Clock.Set("2026-10-14T10:00:00Z"); // week of 12 Oct still running
        var stat = Assert.Single((await h.RetroStats.ComputeAsync(RetroType.Monthly, Oct)).Habits);
        Assert.Equal(1, stat.PeriodsTotal); // only the week of 5 Oct has fully passed
        Assert.Equal(0, stat.PeriodsMet);
    }

    [Fact]
    public async Task Statistics_for_a_period_with_no_data_are_all_zero_and_never_fail()
    {
        using var h = new ServiceHarness();
        var stats = await h.RetroStats.ComputeAsync(RetroType.Annual, new DateOnly(2024, 6, 1));

        Assert.Equal(new DateOnly(2024, 1, 1), stats.PeriodStart);
        Assert.Equal((0, 0), (stats.TasksCompleted, stats.TasksCompletedMinutes));
        Assert.Equal(0, stats.Sprints.SprintCount);
        Assert.Empty(stats.Goals);
        Assert.Empty(stats.Habits);
    }

    [Fact]
    public async Task A_completed_retro_keeps_its_snapshot_while_a_draft_shows_live_numbers()
    {
        using var h = new ServiceHarness();
        var id = await h.Retros.StartOrResumeAsync(RetroType.Monthly, Oct);
        var t1 = await h.Tasks.CreateAsync(new TaskInput("Ett"));
        await h.Tasks.SetStatusAsync(t1, TaskItemStatus.Done);
        Assert.Equal(1, (await h.Retros.GetAsync(id))!.Stats.TasksCompleted);

        await h.Retros.CompleteAsync(id);

        var t2 = await h.Tasks.CreateAsync(new TaskInput("Två"));
        await h.Tasks.SetStatusAsync(t2, TaskItemStatus.Done);

        var detail = (await h.Retros.GetAsync(id))!;
        Assert.True(detail.StatsAreSnapshot);
        Assert.Equal(1, detail.Stats.TasksCompleted); // frozen at completion
        Assert.Equal(2, (await h.RetroStats.ComputeAsync(RetroType.Monthly, Oct)).TasksCompleted);

        await h.Retros.ReopenAsync(id);
        Assert.Equal(2, (await h.Retros.GetAsync(id))!.Stats.TasksCompleted); // live again
    }

    // ---------- actions and follow-up ----------

    [Fact]
    public async Task Actions_and_experiments_have_owner_dates_and_status()
    {
        using var h = new ServiceHarness();
        var retro = await h.Retros.StartOrResumeAsync(RetroType.Monthly, Oct);
        var action = await h.Retros.AddActionAsync(retro, new RetroActionInput(
            "Ingen mejl efter 20", RetroActionKind.Experiment, "Prova i två veckor", "Jag", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 15),
            "Jag sover bättre"));
        var plain = await h.Retros.AddActionAsync(retro, new RetroActionInput("Boka service", Hypothesis: "ignoreras för vanliga åtgärder"));

        var detail = (await h.Retros.GetAsync(retro))!;
        var experiment = detail.Actions.Single(a => a.Id == action);
        Assert.Equal((RetroActionKind.Experiment, "Jag", "Jag sover bättre"), (experiment.Kind, experiment.OwnerName, experiment.Hypothesis));
        Assert.Equal(new DateOnly(2026, 11, 15), experiment.DueDate);
        Assert.Null(detail.Actions.Single(a => a.Id == plain).Hypothesis);

        await AssertCodeAsync(PlanningException.Codes.TitleRequired, () => h.Retros.AddActionAsync(retro, new RetroActionInput(" ")));
        await AssertCodeAsync(PlanningException.Codes.NotFound, () => h.Retros.AddActionAsync(Guid.NewGuid(), new RetroActionInput("x")));
    }

    [Fact]
    public async Task Open_actions_from_earlier_retros_are_offered_for_follow_up_in_the_next_one()
    {
        using var h = new ServiceHarness();
        var sept = await h.Retros.StartOrResumeAsync(RetroType.Monthly, new DateOnly(2026, 9, 1));
        var open = await h.Retros.AddActionAsync(sept, new RetroActionInput("Gå och lägga mig tidigare"));
        var done = await h.Retros.AddActionAsync(sept, new RetroActionInput("Städa garaget"));
        var dropped = await h.Retros.AddActionAsync(sept, new RetroActionInput("Lära mig italienska"));
        await h.Retros.SetActionStatusAsync(done, RetroActionStatus.Done);
        await h.Retros.SetActionStatusAsync(dropped, RetroActionStatus.Dropped);

        var oct = await h.Retros.StartOrResumeAsync(RetroType.Monthly, Oct);
        var revisit = (await h.Retros.GetAsync(oct))!.ToRevisit;
        Assert.Equal([open], revisit.Select(a => a.Id));

        // Revisiting records the outcome; the action stays visible in this retro even after it is closed.
        await h.Retros.RecordFollowUpAsync(open, oct, RetroOutcome.Partly, "Gick bra tre dagar av sju");
        await h.Retros.SetActionStatusAsync(open, RetroActionStatus.Done);

        var after = (await h.Retros.GetAsync(oct))!;
        var shown = Assert.Single(after.ToRevisit);
        var followUp = Assert.Single(shown.FollowUps);
        Assert.Equal((RetroOutcome.Partly, oct), (followUp.Outcome, followUp.RetroId));
        Assert.Equal("Gick bra tre dagar av sju", followUp.Note);
        Assert.Empty(await h.Retros.ListOpenActionsAsync());

        // The retro that created the action does not offer it to itself.
        Assert.DoesNotContain((await h.Retros.GetAsync(sept))!.ToRevisit, a => a.Id == open);
        await AssertCodeAsync(PlanningException.Codes.NotFound, () => h.Retros.RecordFollowUpAsync(Guid.NewGuid(), oct, RetroOutcome.Worked, null));
    }

    [Fact]
    public async Task An_action_can_become_a_backlog_task_once()
    {
        using var h = new ServiceHarness();
        var retro = await h.Retros.StartOrResumeAsync(RetroType.Monthly, Oct);
        var action = await h.Retros.AddActionAsync(retro, new RetroActionInput("Boka service", Details: "Hos cykelhandlaren", DueDate: new DateOnly(2026, 11, 5)));

        var task = await h.Retros.CreateTaskForActionAsync(action);
        var again = await h.Retros.CreateTaskForActionAsync(action);

        Assert.Equal(task, again);
        var created = (await h.Tasks.GetAsync(task))!.Task;
        Assert.Equal("Boka service", created.Title);
        Assert.Equal(new DateOnly(2026, 11, 5), created.DueDate);
        Assert.Equal(task, Assert.Single((await h.Retros.GetAsync(retro))!.Actions).TaskItemId);
    }

    // ---------- related retros, suggestions ----------

    [Fact]
    public async Task An_annual_retro_lists_completed_quarterly_retros_and_works_with_sparse_history()
    {
        using var h = new ServiceHarness();
        var annual = await h.Retros.StartOrResumeAsync(RetroType.Annual, new DateOnly(2026, 3, 1));
        Assert.Empty((await h.Retros.GetAsync(annual))!.Related);   // nothing earlier at all: still fine

        var q2 = await h.Retros.StartOrResumeAsync(RetroType.Quarterly, new DateOnly(2026, 4, 1));
        var q3 = await h.Retros.StartOrResumeAsync(RetroType.Quarterly, new DateOnly(2026, 7, 1));
        await h.Retros.StartOrResumeAsync(RetroType.Quarterly, new DateOnly(2025, 10, 1)); // another year
        await h.Retros.CompleteAsync(q2);
        await h.Retros.CompleteAsync(q3);
        await h.Retros.StartOrResumeAsync(RetroType.Quarterly, new DateOnly(2026, 10, 1));  // draft: not included

        var detail = (await h.Retros.GetAsync(annual))!;
        Assert.Equal([q2, q3], detail.Related.Select(r => r.Id));
        Assert.Null(detail.Previous);
    }

    [Fact]
    public async Task The_previous_completed_retro_of_the_same_type_is_offered_for_comparison()
    {
        using var h = new ServiceHarness();
        var aug = await h.Retros.StartOrResumeAsync(RetroType.Monthly, new DateOnly(2026, 8, 1));
        await h.Retros.CompleteAsync(aug);
        await h.Retros.StartOrResumeAsync(RetroType.Monthly, new DateOnly(2026, 9, 1)); // draft, skipped
        var oct = await h.Retros.StartOrResumeAsync(RetroType.Monthly, Oct);

        Assert.Equal(aug, (await h.Retros.GetAsync(oct))!.Previous!.Id);
    }

    [Fact]
    public async Task Nothing_is_suggested_for_periods_before_the_owner_had_any_data()
    {
        using var h = new ServiceHarness(); // 14 Oct 2026
        Assert.Empty(await h.Retros.SuggestionsAsync());                      // brand new: no data at all

        await h.Tasks.CreateAsync(new LifeOS.Application.Organize.TaskInput("Första uppgiften"));
        Assert.Empty(await h.Retros.SuggestionsAsync());                      // started this month: last month/quarter/year were before that

        h.Clock.Set("2026-11-02T10:00:00Z");
        Assert.Equal([(RetroType.Monthly, new DateOnly(2026, 10, 1))],
            (await h.Retros.SuggestionsAsync()).Select(s => (s.Type, s.PeriodStart)));
    }

    [Fact]
    public async Task Suggestions_cover_the_latest_ended_month_quarter_and_year_and_never_weeks()
    {
        using var h = new ServiceHarness(); // 14 Oct 2026
        h.Clock.Set("2025-03-10T10:00:00Z");
        await h.Tasks.CreateAsync(new LifeOS.Application.Organize.TaskInput("Gammal uppgift")); // the owner has been here since March 2025
        h.Clock.Set(ServiceHarness.Wednesday);
        var suggestions = await h.Retros.SuggestionsAsync();

        Assert.Equal(
            [(RetroType.Monthly, new DateOnly(2026, 9, 1)), (RetroType.Quarterly, new DateOnly(2026, 7, 1)), (RetroType.Annual, new DateOnly(2025, 1, 1))],
            suggestions.Select(s => (s.Type, s.PeriodStart)));
        Assert.DoesNotContain(suggestions, s => s.Type == RetroType.Weekly);

        await h.Retros.StartOrResumeAsync(RetroType.Monthly, new DateOnly(2026, 9, 1));
        Assert.DoesNotContain(await h.Retros.SuggestionsAsync(), s => s.Type == RetroType.Monthly);
    }

    [Fact]
    public void Each_retro_type_has_guided_questions_and_the_keys_are_validated()
    {
        foreach (var type in Enum.GetValues<RetroType>())
        {
            Assert.NotEmpty(RetroQuestions.For(type));
            Assert.All(RetroQuestions.For(type), key => Assert.True(RetroQuestions.IsValid(type, key)));
        }

        Assert.False(RetroQuestions.IsValid(RetroType.Weekly, "year_story"));
    }

    [Fact]
    public async Task One_owner_never_sees_or_changes_another_owners_retros()
    {
        using var h = new ServiceHarness(userId: "user-a");
        var id = await h.Retros.StartOrResumeAsync(RetroType.Monthly, Oct);
        var action = await h.Retros.AddActionAsync(id, new RetroActionInput("Privat"));
        var other = h.ForUser("user-b");

        Assert.Empty(await other.Retros.ListAsync());
        Assert.Null(await other.Retros.GetAsync(id));
        Assert.Empty(await other.Retros.ListOpenActionsAsync());
        await AssertCodeAsync(PlanningException.Codes.NotFound, () => other.Retros.CompleteAsync(id));
        await AssertCodeAsync(PlanningException.Codes.NotFound, () => other.Retros.SetActionStatusAsync(action, RetroActionStatus.Done));

        // Each owner gets their own retro for the same period.
        var theirs = await other.Retros.StartOrResumeAsync(RetroType.Monthly, Oct);
        Assert.NotEqual(id, theirs);
    }
}
