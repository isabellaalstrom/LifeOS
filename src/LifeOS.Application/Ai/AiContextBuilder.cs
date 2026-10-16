using System.Text.Json;
using LifeOS.Application.Abstractions;
using LifeOS.Application.Goals;
using LifeOS.Application.Habits;
using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Application.Retros;
using LifeOS.Domain.Retros;
using Microsoft.Extensions.Options;

namespace LifeOS.Application.Ai;

/// <summary>
/// Builds the small, explicit context sent to the AI provider. Rules: only what the feature needs; titles and numbers,
/// never notes, account details, ids of other records or anything else; built on the server and cut to a hard size limit.
/// </summary>
public sealed class AiContextBuilder(
    RetroService retros,
    RetroStatsService stats,
    GoalService goals,
    HabitService habits,
    SprintService sprints,
    TaskService tasks,
    IUserClock clock,
    IOptions<AiOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        // Swedish text stays readable (and shorter) instead of being \u-escaped.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>What the retro coach may see: this retro's numbers and the owner's own answers, and earlier actions with outcomes.</summary>
    public async Task<string?> RetroAsync(Guid retroId, CancellationToken ct)
    {
        var detail = await retros.GetAsync(retroId, ct);
        if (detail is null)
        {
            return null;
        }

        var r = detail.Retro;
        var s = detail.Stats;
        var context = new
        {
            retro = new { type = r.Type.ToString(), period = r.PeriodStart.ToString("yyyy-MM-dd"), inProgress = s.InProgress },
            numbers = new
            {
                tasksCompleted = s.TasksCompleted,
                minutesInCompletedTasks = s.TasksCompletedMinutes,
                sprints = new
                {
                    count = s.Sprints.SprintCount,
                    plannedFromStart = s.Sprints.Planned,
                    addedDuringWeeks = s.Sprints.AddedAfterStart,
                    removed = s.Sprints.Removed,
                    carriedOver = s.Sprints.CarriedOut,
                    done = s.Sprints.Completed,
                },
                goals = s.Goals.Select(g => new { g.Title, startPercent = g.StartPercent, endPercent = g.EndPercent, g.AchievedInPeriod }),
                habits = s.Habits.Select(h => new { h.Name, periodsMet = h.PeriodsMet, periodsTotal = h.PeriodsTotal, completions = h.Completions }),
            },
            wellbeingOneToFive = new { r.Energy, r.Balance, r.Stress, r.Satisfaction },
            ownerAnswers = detail.Answers.ToDictionary(a => a.Key, a => Cut(a.Value, 1200)),
            ownerSummary = Cut(r.Summary, 1500),
            earlierActions = detail.ToRevisit.Select(a => new
            {
                a.Title,
                kind = a.Kind.ToString(),
                status = a.Status.ToString(),
                followUps = a.FollowUps.Select(f => new { outcome = f.Outcome.ToString(), note = Cut(f.Note, 300) }),
            }),
            earlierRetros = detail.Related.Select(x => new { period = x.PeriodStart.ToString("yyyy-MM-dd"), summary = Cut(x.Summary, 600) }),
        };

        return Limit(JsonSerializer.Serialize(context, Json));
    }

    /// <summary>What the sprint suggestion may see: capacity, what is planned, and the unplanned backlog numbered 1..n (titles only).</summary>
    public async Task<(string Json, IReadOnlyList<TaskView> Candidates)> SprintAsync(DateOnly week, CancellationToken ct)
    {
        var board = await sprints.GetBoardAsync(week, ct);
        var candidates = (await tasks.ListPlannableAsync(null, ct)).Take(40).ToList();
        var context = new
        {
            week = board.WeekStart.ToString("yyyy-MM-dd"),
            capacityMinutes = board.CapacityMinutes,
            plannedMinutes = board.PlannedMinutes,
            alreadyPlanned = board.ActiveTasks.Select(t => new { t.Task.Title, minutes = t.Task.EstimateMinutes }),
            backlogCandidates = candidates.Select((t, i) => new
            {
                n = i + 1,
                t.Title,
                minutes = t.EstimateMinutes,
                priority = t.Priority.ToString(),
                due = t.DueDate?.ToString("yyyy-MM-dd"),
                goal = t.GoalTitle,
            }),
        };

        return (Limit(JsonSerializer.Serialize(context, Json)), candidates);
    }

    /// <summary>What a dashboard insight may see: counts and percentages only. No task titles, notes or reflections.</summary>
    public async Task<string> InsightAsync(CancellationToken ct)
    {
        var thisMonth = await stats.ComputeAsync(RetroType.Monthly, clock.Today, ct);
        var lastMonth = await stats.ComputeAsync(RetroType.Monthly, RetroPeriods.Previous(RetroType.Monthly, thisMonth.PeriodStart), ct);
        var currentGoals = await goals.ListCurrentAsync(8, ct);
        var habitViews = await habits.ListAsync(false, ct);
        var recent = (await retros.ListAsync(null, ct)).Where(r => r.Status == RetroStatus.Completed).Take(3).ToList();

        object Month(RetroStats m) => new
        {
            period = m.PeriodStart.ToString("yyyy-MM"),
            tasksCompleted = m.TasksCompleted,
            plannedFromStart = m.Sprints.Planned,
            addedDuringWeeks = m.Sprints.AddedAfterStart,
            carriedOver = m.Sprints.CarriedOut,
            sprintTasksDone = m.Sprints.Completed,
            habitPeriodsMet = m.Habits.Sum(h => h.PeriodsMet),
            habitPeriodsTotal = m.Habits.Sum(h => h.PeriodsTotal),
        };

        var context = new
        {
            today = clock.Today.ToString("yyyy-MM-dd"),
            thisMonthSoFar = Month(thisMonth),
            lastMonth = Month(lastMonth),
            goals = currentGoals.Select(g => new { g.Title, percent = g.Percent, mode = g.Mode.ToString() }),
            habits = habitViews.Select(h => new { h.Name, doneThisPeriod = h.CountInPeriod, target = h.TargetCount, streak = h.Streak }),
            recentWellbeingOneToFive = recent.Select(r => new { period = r.PeriodStart.ToString("yyyy-MM-dd"), r.Energy, r.Balance, r.Stress, r.Satisfaction }),
        };

        return Limit(JsonSerializer.Serialize(context, Json));
    }

    private string Limit(string json) => json.Length <= options.Value.MaxContextChars ? json : json[..options.Value.MaxContextChars];

    private static string? Cut(string? text, int max) => string.IsNullOrWhiteSpace(text) ? null : text.Length <= max ? text : text[..max];
}
