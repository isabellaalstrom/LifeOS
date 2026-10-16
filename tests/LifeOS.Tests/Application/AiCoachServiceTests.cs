using LifeOS.Application.Abstractions;
using LifeOS.Application.Ai;
using LifeOS.Application.Goals;
using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Application.Retros;
using LifeOS.Domain.Ai;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Retros;
using LifeOS.Domain.Tasks;
using LifeOS.Tests.Infrastructure;

namespace LifeOS.Tests.Application;

public class AiCoachServiceTests
{
    private static readonly DateOnly Oct = new(2026, 10, 1);
    private static readonly DateOnly Week = ServiceHarness.Week;

    private const string FollowUpJson =
        """{"observations":["Du blev klar med tre uppgifter."],"hypotheses":["Kanske gav korta pass mer energi."],"question":"Vad gav dig mest energi?"}""";

    private static async Task AssertCodeAsync(string code, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<PlanningException>(action);
        Assert.Equal(code, ex.Code);
    }

    private static async Task<Guid> RetroWithAnswersAsync(ServiceHarness h)
    {
        var id = await h.Retros.StartOrResumeAsync(RetroType.Monthly, Oct);
        await h.Retros.SaveAsync(id,
            new Dictionary<string, string?> { ["highlights"] = "Första långpasset var fantastiskt" },
            "Jag vill vara mer snäll mot mig själv", new WellbeingInput(4, null, 2, null));
        return id;
    }

    // ---------- availability and fallbacks ----------

    [Fact]
    public async Task Ai_is_off_by_default_and_the_provider_is_never_called()
    {
        using var h = new ServiceHarness();
        var retro = await RetroWithAnswersAsync(h);

        var result = await h.Coach.RetroFollowUpAsync(retro, null, null);

        Assert.Equal(AiStatus.Disabled, result.Status);
        Assert.Empty(h.AiClient.Requests);
        var availability = await h.Coach.GetAvailabilityAsync();
        Assert.True(availability.ProviderConfigured);
        Assert.False(availability.EnabledByOwner);
        Assert.False(availability.CanUse);
    }

    [Fact]
    public async Task Without_a_configured_provider_every_feature_reports_not_configured_and_nothing_else_happens()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        h.AiClient.IsConfigured = false;
        var retro = await RetroWithAnswersAsync(h);
        await h.NewPlannableTaskAsync("Något");

        Assert.Equal(AiStatus.NotConfigured, (await h.Coach.RetroFollowUpAsync(retro, null, null)).Status);
        Assert.Equal(AiStatus.NotConfigured, (await h.Coach.ProposeRetroActionsAsync(retro, null)).Status);
        Assert.Equal(AiStatus.NotConfigured, (await h.Coach.FormulateGoalAsync("springa mer", GoalHorizon.Quarter)).Status);
        Assert.Equal(AiStatus.NotConfigured, (await h.Coach.SuggestSprintAsync(Week)).Status);
        Assert.Equal(AiStatus.NotConfigured, (await h.Coach.GenerateInsightAsync(false)).Status);
        Assert.Empty(h.AiClient.Requests);
        Assert.False((await h.Coach.GetAvailabilityAsync()).CanUse);
        Assert.Null(await h.Coach.GetCachedInsightAsync());
    }

    [Theory]
    [InlineData(AiFailure.RateLimited, AiStatus.RateLimited)]
    [InlineData(AiFailure.Unavailable, AiStatus.Failed)]
    [InlineData(AiFailure.Rejected, AiStatus.Failed)]
    [InlineData(AiFailure.BadResponse, AiStatus.Failed)]
    public async Task Provider_failures_become_a_status_and_change_nothing(AiFailure failure, AiStatus expected)
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        var retro = await RetroWithAnswersAsync(h);
        h.AiClient.EnqueueFailure(failure);

        var result = await h.Coach.RetroFollowUpAsync(retro, null, "Hej");

        Assert.Equal(expected, result.Status);
        Assert.Empty(await h.Coach.GetRetroChatAsync(retro)); // not even the owner's message is stored for a failed call
    }

    [Fact]
    public async Task The_daily_limit_stops_calls_without_contacting_the_provider()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        h.AiOptions.DailyLimit = 2;
        var retro = await RetroWithAnswersAsync(h);
        h.AiClient.Enqueue(FollowUpJson);
        h.AiClient.Enqueue(FollowUpJson);
        h.AiClient.Enqueue(FollowUpJson);

        Assert.True((await h.Coach.RetroFollowUpAsync(retro, null, null)).Ok);
        Assert.True((await h.Coach.RetroFollowUpAsync(retro, null, null)).Ok);
        var third = await h.Coach.RetroFollowUpAsync(retro, null, null);

        Assert.Equal(AiStatus.RateLimited, third.Status);
        Assert.Equal(2, h.AiClient.Requests.Count);
        var availability = await h.Coach.GetAvailabilityAsync();
        Assert.Equal((2, 2), (availability.CallsToday, availability.DailyLimit));
        Assert.False(availability.CanUse);

        // A new local day starts fresh.
        h.Clock.Set("2026-10-15T10:00:00Z");
        Assert.True((await h.Coach.GetAvailabilityAsync()).CanUse);
    }

    // ---------- retro coach ----------

    [Fact]
    public async Task The_coach_keeps_observations_and_hypotheses_apart_and_the_conversation_is_stored_with_the_retro()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync(AiCoachMode.Reflective);
        var retro = await RetroWithAnswersAsync(h);
        h.AiClient.Enqueue(FollowUpJson);

        var result = await h.Coach.RetroFollowUpAsync(retro, null, null);

        Assert.True(result.Ok);
        Assert.Equal(["Du blev klar med tre uppgifter."], result.Value!.Observations);
        Assert.Equal(["Kanske gav korta pass mer energi."], result.Value.Hypotheses);
        Assert.Equal("Vad gav dig mest energi?", result.Value.Question);

        // The system prompt carries the rules: Swedish, observed vs hypothesis, no failure framing, cannot change anything.
        var system = h.AiClient.Requests[0].System;
        Assert.Contains("svenska", system);
        Assert.Contains("observationer", system);
        Assert.Contains("hypoteser", system);
        Assert.Contains("misslyckanden", system);
        Assert.Contains("förslag", system);
        Assert.Contains("reflekterande", system); // mode instruction follows the owner's preference

        h.AiClient.Enqueue(FollowUpJson);
        await h.Coach.RetroFollowUpAsync(retro, AiCoachMode.Analytical, "Det var nog långpasset som gav energi.");
        Assert.Contains("analytisk", h.AiClient.Requests[1].System);

        var chat = await h.Coach.GetRetroChatAsync(retro);
        Assert.Equal([false, true, false], chat.Select(m => m.IsUser));
        Assert.Equal("Det var nog långpasset som gav energi.", chat[1].Content);
        Assert.Equal(["Du blev klar med tre uppgifter."], chat[0].Observations);
        Assert.Equal(["Kanske gav korta pass mer energi."], chat[0].Hypotheses);
        // The earlier exchange is passed along so the follow-up question can build on it.
        Assert.Contains("Vad gav dig mest energi?", h.AiClient.Requests[1].Messages.Select(m => m.Content));
    }

    [Fact]
    public async Task Fenced_json_is_accepted_and_unusable_answers_fail_cleanly_without_storing_anything()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        var retro = await RetroWithAnswersAsync(h);

        h.AiClient.Enqueue("```json\n" + FollowUpJson + "\n```");
        Assert.True((await h.Coach.RetroFollowUpAsync(retro, null, null)).Ok);

        foreach (var junk in new[] { "Här är mitt svar: hej!", "[1,2,3]", """{"unrelated":true}""", "{}" })
        {
            h.AiClient.Enqueue(junk);
            Assert.Equal(AiStatus.Failed, (await h.Coach.RetroFollowUpAsync(retro, null, "Svar")).Status);
        }

        Assert.Single(await h.Coach.GetRetroChatAsync(retro)); // only the one good reply
    }

    // ---------- privacy: what is sent ----------

    [Fact]
    public async Task The_retro_context_contains_the_owners_answers_but_no_account_data_notes_or_task_titles()
    {
        using var h = new ServiceHarness(userId: "user-a");
        await h.EnableAiAsync();
        await h.Tasks.CreateAsync(new TaskInput("HEMLIGT-UPPGIFTSNAMN", Notes: "HEMLIG-ANTECKNING-123"));
        var retro = await RetroWithAnswersAsync(h);
        h.AiClient.Enqueue(FollowUpJson);

        await h.Coach.RetroFollowUpAsync(retro, null, null);

        var sent = h.AiClient.LastSentText;
        Assert.Contains("Första långpasset var fantastiskt", sent);       // what the owner chose to reflect on
        Assert.Contains("Jag vill vara mer snäll mot mig själv", sent);
        Assert.DoesNotContain("HEMLIGT-UPPGIFTSNAMN", sent);               // task titles are not needed for a retro
        Assert.DoesNotContain("HEMLIG-ANTECKNING-123", sent);
        Assert.DoesNotContain("user-a", sent);                             // no account identifiers
        Assert.DoesNotContain("@", sent);
    }

    [Fact]
    public async Task The_dashboard_insight_context_has_numbers_only()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        var task = await h.Tasks.CreateAsync(new TaskInput("HEMLIGT-UPPGIFTSNAMN", Notes: "HEMLIG-ANTECKNING-123", EstimateMinutes: 30));
        await h.Tasks.SetStatusAsync(task, TaskItemStatus.Done);
        await h.Goals.CreateAsync(new GoalInput("Cykla mer", GoalHorizon.Quarter, Oct, GoalProgressMode.Manual));
        var retro = await RetroWithAnswersAsync(h);
        await h.Retros.CompleteAsync(retro);
        h.AiClient.Enqueue("""{"observations":["Tre uppgifter klara."],"hypotheses":[]}""");

        var result = await h.Coach.GenerateInsightAsync(false);

        Assert.True(result.Ok);
        var sent = h.AiClient.LastSentText;
        Assert.Contains("tasksCompleted", sent);
        Assert.Contains("Cykla mer", sent);                        // goal titles give the numbers meaning
        Assert.DoesNotContain("HEMLIGT-UPPGIFTSNAMN", sent);
        Assert.DoesNotContain("HEMLIG-ANTECKNING-123", sent);
        Assert.DoesNotContain("Första långpasset", sent);          // retro text is not part of a dashboard insight
        Assert.DoesNotContain("snäll mot mig själv", sent);
    }

    [Fact]
    public async Task The_sprint_context_sends_candidate_titles_numbered_and_never_notes()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        await h.Tasks.CreateAsync(new TaskInput("Boka service", Notes: "HEMLIG-ANTECKNING-123", EstimateMinutes: 45));
        h.AiClient.Enqueue("""{"picks":[]}""");

        await h.Coach.SuggestSprintAsync(Week);

        var sent = h.AiClient.LastSentText;
        Assert.Contains("Boka service", sent);
        Assert.Contains("\"n\":1", sent);
        Assert.DoesNotContain("HEMLIG-ANTECKNING-123", sent);
    }

    [Fact]
    public async Task The_context_is_cut_to_the_configured_size()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        h.AiOptions.MaxContextChars = 400;
        var retro = await h.Retros.StartOrResumeAsync(RetroType.Monthly, Oct);
        await h.Retros.SaveAsync(retro, new Dictionary<string, string?> { ["highlights"] = new string('x', 5000) }, null, new WellbeingInput(null, null, null, null));
        h.AiClient.Enqueue(FollowUpJson);

        await h.Coach.RetroFollowUpAsync(retro, null, null);

        var context = h.AiClient.Requests[0].Messages[0].Content;
        Assert.True(context.Length <= 400 + "Underlag (JSON):\n".Length);
    }

    // ---------- proposals: the AI never writes ----------

    [Fact]
    public async Task Suggested_retro_actions_are_pending_proposals_and_nothing_exists_until_accepted()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        var retro = await RetroWithAnswersAsync(h);
        h.AiClient.Enqueue("""
            {"actions":[
              {"title":"Planera in ett vilopass","kind":"Action","details":"Varje vecka","rationale":"Energin sjönk mot slutet."},
              {"title":"Ingen skärm efter 21","kind":"Experiment","hypothesis":"Jag sover bättre","rationale":"Sömn nämndes."}]}
            """);

        var result = await h.Coach.ProposeRetroActionsAsync(retro, null);

        Assert.True(result.Ok);
        Assert.Equal(2, result.Value!.Count);
        Assert.Empty(await h.Retros.ListOpenActionsAsync());                       // nothing was created by the AI
        var pending = await h.Proposals.ListAsync(ProposalContext.Retro(retro));
        Assert.Equal(["Planera in ett vilopass", "Ingen skärm efter 21"], pending.Select(p => p.Summary));
        Assert.All(pending, p => Assert.Equal(AiProposalStatus.Pending, p.Status));
        Assert.Equal("Energin sjönk mot slutet.", pending[0].Rationale);

        await h.Proposals.AcceptAsync(pending[1].Id);
        await h.Proposals.RejectAsync(pending[0].Id);

        var action = Assert.Single(await h.Retros.ListOpenActionsAsync());
        Assert.Equal((RetroActionKind.Experiment, "Jag sover bättre"), (action.Kind, action.Hypothesis));
        Assert.Empty(await h.Proposals.ListAsync(ProposalContext.Retro(retro)));    // none pending any more
        Assert.Equal(2, (await h.Proposals.ListAsync(ProposalContext.Retro(retro), pendingOnly: false)).Count);

        await AssertCodeAsync(PlanningException.Codes.ProposalDecided, () => h.Proposals.AcceptAsync(pending[1].Id));
        await AssertCodeAsync(PlanningException.Codes.ProposalDecided, () => h.Proposals.RejectAsync(pending[0].Id));
    }

    [Fact]
    public async Task Unusable_suggestions_are_ignored_and_at_most_three_are_kept()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        var retro = await RetroWithAnswersAsync(h);
        h.AiClient.Enqueue("""{"actions":[{"details":"saknar titel"},{"title":"  "},{"title":"A"},{"title":"B"},{"title":"C"},{"title":"D"}]}""");

        var result = await h.Coach.ProposeRetroActionsAsync(retro, null);

        Assert.Equal(["A", "B", "C"], (await h.Proposals.ListAsync(ProposalContext.Retro(retro))).Select(p => p.Summary));
        Assert.Equal(3, result.Value!.Count);

        h.AiClient.Enqueue("""{"actions":[{"details":"bara skräp"}]}""");
        Assert.Equal(AiStatus.Failed, (await h.Coach.ProposeRetroActionsAsync(retro, null)).Status);
    }

    [Fact]
    public async Task Sprint_suggestions_accept_only_numbers_that_refer_to_a_candidate_and_accepting_plans_the_task()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        var a = await h.NewPlannableTaskAsync("Första", 30);
        var b = await h.NewPlannableTaskAsync("Andra", 60);
        await h.NewPlannableTaskAsync("Tredje", 90);
        h.AiClient.Enqueue("""{"picks":[{"n":2,"reason":"Passar kapaciteten"},{"n":99,"reason":"finns inte"},{"n":2,"reason":"dubblett"},{"n":"x"},{"n":0}]}""");

        var result = await h.Coach.SuggestSprintAsync(Week);

        Assert.True(result.Ok);
        Assert.Single(result.Value!);
        Assert.Empty((await h.Sprints.GetBoardAsync(Week)).ActiveTasks);            // suggestions alone change no sprint
        var proposal = Assert.Single(await h.Proposals.ListAsync(ProposalContext.Sprint(Week)));
        Assert.Equal("Andra", proposal.Summary);
        Assert.Equal("Passar kapaciteten", proposal.Rationale);

        await h.Proposals.AcceptAsync(proposal.Id);

        var board = await h.Sprints.GetBoardAsync(Week);
        Assert.Equal([b], board.ActiveTasks.Select(t => t.Task.Id));
        Assert.DoesNotContain(board.ActiveTasks, t => t.Task.Id == a);
    }

    [Fact]
    public async Task A_proposal_that_no_longer_applies_stays_pending_with_the_reason()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        var task = await h.NewPlannableTaskAsync("Flyttad under tiden");
        h.AiClient.Enqueue("""{"picks":[{"n":1,"reason":"ok"}]}""");
        await h.Coach.SuggestSprintAsync(Week);
        var proposal = Assert.Single(await h.Proposals.ListAsync(ProposalContext.Sprint(Week)));

        await h.Sprints.AddTaskAsync(ServiceHarness.NextWeek, task); // the owner planned it elsewhere meanwhile

        await AssertCodeAsync(PlanningException.Codes.PlannedInOtherSprint, () => h.Proposals.AcceptAsync(proposal.Id));

        var still = Assert.Single(await h.Proposals.ListAsync(ProposalContext.Sprint(Week)));
        Assert.Equal(AiProposalStatus.Pending, still.Status);
        Assert.Equal(PlanningException.Codes.PlannedInOtherSprint, still.LastError);
        await h.Proposals.RejectAsync(proposal.Id); // and the owner can simply dismiss it
        Assert.Empty(await h.Proposals.ListAsync(ProposalContext.Sprint(Week)));
    }

    [Fact]
    public async Task Goal_formulation_returns_a_suggestion_and_saves_nothing()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        h.AiClient.Enqueue("""
            {"title":"Höj min FTP till 280 W","description":"Systematisk träning","mode":"Numeric","start":240,"target":280,"unit":"W","milestones":["Test i november","Test i januari"]}
            """);

        var result = await h.Coach.FormulateGoalAsync("Jag vill bli starkare på cykeln", GoalHorizon.Quarter);

        Assert.True(result.Ok);
        var s = result.Value!;
        Assert.Equal(("Höj min FTP till 280 W", GoalProgressMode.Numeric, 240m, 280m, "W"), (s.Title, s.Mode, s.Start, s.Target, s.Unit));
        Assert.Equal(GoalHorizon.Quarter, s.Horizon);
        Assert.Equal(2, s.Milestones.Count);
        Assert.Empty(await h.Goals.ListAsync(includeClosed: true));               // a suggestion only pre-fills a form

        h.AiClient.Enqueue("""{"title":"Cykla mer","mode":"Numeric","start":null,"target":null}""");
        var downgraded = await h.Coach.FormulateGoalAsync("cykla mer", GoalHorizon.Month);
        Assert.Equal(GoalProgressMode.Manual, downgraded.Value!.Mode);             // numeric without numbers is not usable
        Assert.Null(downgraded.Value.Start);

        await AssertCodeAsync(PlanningException.Codes.TitleRequired, () => h.Coach.FormulateGoalAsync("  ", GoalHorizon.Year));
    }

    [Fact]
    public async Task An_accepted_goal_proposal_is_validated_by_the_normal_goal_service()
    {
        using var h = new ServiceHarness();
        var parent = Guid.NewGuid();
        var bad = await h.Proposals.CreateGoalAsync(
            new GoalPayload("Delmål", null, GoalHorizon.Month, new DateOnly(2026, 10, 1), GoalProgressMode.Numeric, null, 10, null, null),
            "Ogiltigt: saknar startvärde", null, "test");
        await AssertCodeAsync(PlanningException.Codes.InvalidNumeric, () => h.Proposals.AcceptAsync(bad));
        Assert.Empty(await h.Goals.ListAsync(includeClosed: true));

        var good = await h.Proposals.CreateGoalAsync(
            new GoalPayload("Vilodagar", "Minst en per vecka", GoalHorizon.Quarter, new DateOnly(2026, 10, 1), GoalProgressMode.Milestones, null, null, null, ["Första veckan", "Andra veckan"]),
            "Vilodagar", null, "test");
        await h.Proposals.AcceptAsync(good);

        var goal = Assert.Single(await h.Goals.ListAsync());
        Assert.Equal(2, (await h.Goals.GetAsync(goal.Id))!.Milestones.Count);
        Assert.NotEqual(Guid.Empty, parent);
    }

    // ---------- insights ----------

    [Fact]
    public async Task An_insight_is_cached_and_only_regenerated_when_the_data_or_the_time_changed()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        var json = """{"observations":["Observation"],"hypotheses":["Kanske så här."]}""";
        h.AiClient.Enqueue(json);
        h.AiClient.Enqueue(json);
        h.AiClient.Enqueue(json);
        h.AiClient.Enqueue(json);

        var first = await h.Coach.GenerateInsightAsync(false);
        Assert.False(first.Value!.FromCache);

        var again = await h.Coach.GenerateInsightAsync(false);   // same data, fresh: no provider call
        Assert.True(again.Value!.FromCache);
        Assert.Single(h.AiClient.Requests);

        var forcedTooSoon = await h.Coach.GenerateInsightAsync(true); // "again" within five minutes is also served from cache
        Assert.True(forcedTooSoon.Value!.FromCache);
        Assert.Single(h.AiClient.Requests);

        h.Clock.Set("2026-10-14T10:06:00Z");
        Assert.False((await h.Coach.GenerateInsightAsync(true)).Value!.FromCache);          // forced after five minutes
        Assert.Equal(2, h.AiClient.Requests.Count);

        var task = await h.Tasks.CreateAsync(new TaskInput("Ny"));
        await h.Tasks.SetStatusAsync(task, TaskItemStatus.Done);                             // the data changed
        Assert.False((await h.Coach.GenerateInsightAsync(false)).Value!.FromCache);
        Assert.Equal(3, h.AiClient.Requests.Count);

        h.Clock.Set("2026-10-15T01:00:00Z");                                                  // over 12 hours later
        Assert.False((await h.Coach.GenerateInsightAsync(false)).Value!.FromCache);
        Assert.Equal(4, h.AiClient.Requests.Count);
    }

    [Fact]
    public async Task The_cached_insight_can_be_read_without_calling_the_provider_even_when_ai_is_off()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        h.AiClient.Enqueue("""{"observations":["A"],"hypotheses":["B"]}""");
        await h.Coach.GenerateInsightAsync(false);
        await h.Prefs.SetAiAsync(false, AiCoachMode.Balanced);

        var cached = await h.Coach.GetCachedInsightAsync();

        Assert.Equal(["A"], cached!.Observations);
        Assert.Equal(["B"], cached.Hypotheses);
        Assert.Equal(AiStatus.Disabled, (await h.Coach.GenerateInsightAsync(true)).Status);
        Assert.Single(h.AiClient.Requests);
    }

    // ---------- the AI cannot change data ----------

    [Fact]
    public async Task Using_every_ai_feature_changes_no_goals_tasks_sprints_or_actions()
    {
        using var h = new ServiceHarness();
        await h.EnableAiAsync();
        var task = await h.NewPlannableTaskAsync("Kandidat");
        var retro = await RetroWithAnswersAsync(h);
        h.AiClient.Enqueue(FollowUpJson);
        h.AiClient.Enqueue("""{"actions":[{"title":"Åtgärd","kind":"Action"}]}""");
        h.AiClient.Enqueue("""{"title":"Mål","mode":"Manual"}""");
        h.AiClient.Enqueue("""{"picks":[{"n":1,"reason":"x"}]}""");
        h.AiClient.Enqueue("""{"observations":["A"],"hypotheses":[]}""");

        await h.Coach.RetroFollowUpAsync(retro, null, "Svar");
        await h.Coach.ProposeRetroActionsAsync(retro, null);
        await h.Coach.FormulateGoalAsync("något", GoalHorizon.Month);
        await h.Coach.SuggestSprintAsync(Week);
        await h.Coach.GenerateInsightAsync(false);

        Assert.Empty(await h.Goals.ListAsync(includeClosed: true));
        Assert.Empty(await h.Retros.ListOpenActionsAsync());
        Assert.Empty((await h.Sprints.GetBoardAsync(Week)).Tasks);
        Assert.Equal(TaskItemStatus.ToDo, (await h.Tasks.GetAsync(task))!.Task.Status);
        Assert.Equal(2, (await h.Proposals.CountPendingAsync()));                    // only the proposals exist, waiting for the owner
    }

    [Fact]
    public async Task One_owner_cannot_see_or_decide_another_owners_proposals_or_chat()
    {
        using var h = new ServiceHarness(userId: "user-a");
        await h.EnableAiAsync();
        var retro = await RetroWithAnswersAsync(h);
        h.AiClient.Enqueue("""{"actions":[{"title":"Privat"}]}""");
        await h.Coach.ProposeRetroActionsAsync(retro, null);
        var mine = Assert.Single(await h.Proposals.ListAsync(ProposalContext.Retro(retro)));

        var other = h.ForUser("user-b");
        Assert.Empty(await other.Proposals.ListAsync(ProposalContext.Retro(retro)));
        Assert.Equal(0, await other.Proposals.CountPendingAsync());
        await AssertCodeAsync(PlanningException.Codes.NotFound, () => other.Proposals.AcceptAsync(mine.Id));
        await AssertCodeAsync(PlanningException.Codes.NotFound, () => other.Proposals.RejectAsync(mine.Id));
        Assert.Empty(await other.Coach.GetRetroChatAsync(retro));
        Assert.False((await other.Coach.GetAvailabilityAsync()).EnabledByOwner);          // preferences are per owner too
    }

    [Fact]
    public async Task Ai_preferences_default_to_off_and_balanced_and_are_stored_per_owner()
    {
        using var h = new ServiceHarness(userId: "user-a");
        var initial = await h.Prefs.GetAiAsync();
        Assert.False(initial.Enabled);
        Assert.Equal(AiCoachMode.Balanced, initial.Mode);

        await h.Prefs.SetAiAsync(true, AiCoachMode.Analytical);
        await h.Prefs.SetAiAsync(true, AiCoachMode.Analytical); // saving twice must not create a second row
        Assert.Equal((true, AiCoachMode.Analytical), ((await h.Prefs.GetAiAsync()).Enabled, (await h.Prefs.GetAiAsync()).Mode));
        Assert.False((await h.ForUser("user-b").Prefs.GetAiAsync()).Enabled);
    }

    [Fact]
    public async Task Time_zone_changes_are_validated()
    {
        using var h = new ServiceHarness();
        Assert.Equal("Europe/Stockholm", await h.Prefs.GetTimeZoneAsync());

        await h.Prefs.SetTimeZoneAsync("Europe/London");
        Assert.Equal("Europe/London", await h.Prefs.GetTimeZoneAsync());

        await AssertCodeAsync(PlanningException.Codes.InvalidTimeZone, () => h.Prefs.SetTimeZoneAsync("Mars/Olympus"));
        await AssertCodeAsync(PlanningException.Codes.InvalidTimeZone, () => h.Prefs.SetTimeZoneAsync(""));
        Assert.Equal("Europe/London", await h.Prefs.GetTimeZoneAsync());
    }
}
