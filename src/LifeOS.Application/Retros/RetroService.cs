using System.Text.Json;
using LifeOS.Application.Abstractions;
using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Domain.Retros;
using LifeOS.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Retros;

/// <summary>
/// Retrospectives: start any time, save as a draft, resume, complete. No AI is needed for any of it.
/// Weekly retros are always optional and are never suggested.
/// </summary>
public sealed class RetroService(IAppDbContextFactory dbFactory, IUserClock clock, RetroStatsService stats, TaskService tasks)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<RetroView>> ListAsync(RetroType? type = null, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var query = db.Retrospectives.AsQueryable();
        if (type is { } t)
        {
            query = query.Where(r => r.Type == t);
        }

        var retros = await query.OrderByDescending(r => r.PeriodStart).ThenBy(r => r.CreatedUtc).ToListAsync(ct);
        var counts = await db.RetroResponses.GroupBy(r => r.RetroId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return retros.Select(r => ToView(r, counts.GetValueOrDefault(r.Id))).ToList();
    }

    /// <summary>Periods that have ended without a retro: the latest month, quarter and year. Weekly is never suggested.</summary>
    public async Task<IReadOnlyList<RetroSuggestion>> SuggestionsAsync(CancellationToken ct = default)
    {
        var today = clock.Today;
        await using var db = dbFactory.Create();
        var existing = await db.Retrospectives.Select(r => new { r.Type, r.PeriodStart }).ToListAsync(ct);

        // Only periods the owner was actually using the app in: nothing is suggested before the first piece of data exists.
        var firstUtc = new[]
        {
            await db.Tasks.OrderBy(t => t.CreatedUtc).Select(t => (DateTimeOffset?)t.CreatedUtc).FirstOrDefaultAsync(ct),
            await db.Goals.OrderBy(g => g.CreatedUtc).Select(g => (DateTimeOffset?)g.CreatedUtc).FirstOrDefaultAsync(ct),
            await db.Habits.OrderBy(h => h.CreatedUtc).Select(h => (DateTimeOffset?)h.CreatedUtc).FirstOrDefaultAsync(ct),
        }.Where(d => d is not null).Select(d => d!.Value).DefaultIfEmpty().Min();
        if (firstUtc == default)
        {
            return [];
        }

        var firstDay = PeriodCalculator.LocalDate(firstUtc, clock.Zone);

        var suggestions = new List<RetroSuggestion>();
        foreach (var type in new[] { RetroType.Monthly, RetroType.Quarterly, RetroType.Annual })
        {
            var last = RetroPeriods.Previous(type, RetroPeriods.Start(type, today));
            if (RetroPeriods.EndExclusive(type, last) <= firstDay)
            {
                continue;
            }

            if (!existing.Any(e => e.Type == type && e.PeriodStart == last))
            {
                suggestions.Add(new RetroSuggestion(type, last));
            }
        }

        return suggestions;
    }

    /// <summary>Returns the retro for the period, creating a draft if there is none yet. Safe to call repeatedly.</summary>
    public async Task<Guid> StartOrResumeAsync(RetroType type, DateOnly anyDateInPeriod, CancellationToken ct = default)
    {
        var start = RetroPeriods.Start(type, anyDateInPeriod);
        await using var db = dbFactory.Create();
        var existing = await db.Retrospectives.FirstOrDefaultAsync(r => r.Type == type && r.PeriodStart == start, ct);
        if (existing is not null)
        {
            return existing.Id;
        }

        var retro = new Retrospective { Type = type, PeriodStart = start };
        db.Retrospectives.Add(retro);
        await db.SaveChangesAsync(ct);
        return retro.Id;
    }

    public async Task<RetroDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        RetroStats? computeFor = null;
        RetroDetail? detail;

        await using (var db = dbFactory.Create())
        {
            var retro = await db.Retrospectives.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (retro is null)
            {
                return null;
            }

            var answers = await db.RetroResponses.Where(r => r.RetroId == id)
                .ToDictionaryAsync(r => r.QuestionKey, r => r.Answer, ct);
            var view = ToView(retro, answers.Count);

            var snapshot = retro.Status == RetroStatus.Completed && retro.StatsSnapshotJson is { } json
                ? JsonSerializer.Deserialize<RetroStats>(json, Json)
                : null;
            if (snapshot is null)
            {
                computeFor = new RetroStats(retro.PeriodStart, view.PeriodEndExclusive, false, 0, 0, new RetroSprintStats(0, 0, 0, 0, 0, 0, 0), [], []);
            }

            var followedUpHere = await db.RetroActionFollowUps.Where(f => f.RetroId == id).Select(f => f.ActionId).Distinct().ToListAsync(ct);
            var revisit = await db.RetroActions
                .Where(a => a.RetroId != id && ((a.Status == RetroActionStatus.Open || a.Status == RetroActionStatus.InProgress) || followedUpHere.Contains(a.Id)))
                .OrderBy(a => a.DueDate == null).ThenBy(a => a.DueDate).ThenBy(a => a.CreatedUtc)
                .ToListAsync(ct);
            var own = await db.RetroActions.Where(a => a.RetroId == id).OrderBy(a => a.CreatedUtc).ToListAsync(ct);
            var actionIds = revisit.Select(a => a.Id).Concat(own.Select(a => a.Id)).ToList();
            var followUps = await db.RetroActionFollowUps
                .Where(f => actionIds.Contains(f.ActionId))
                .OrderBy(f => f.RecordedUtc).ToListAsync(ct);

            var smaller = RetroPeriods.SmallerType(retro.Type);
            var related = smaller is { } s
                ? await db.Retrospectives
                    .Where(r => r.Type == s && r.PeriodStart >= retro.PeriodStart && r.PeriodStart < view.PeriodEndExclusive && r.Status == RetroStatus.Completed)
                    .OrderBy(r => r.PeriodStart).ToListAsync(ct)
                : [];
            var previous = await db.Retrospectives
                .Where(r => r.Type == retro.Type && r.PeriodStart < retro.PeriodStart && r.Status == RetroStatus.Completed)
                .OrderByDescending(r => r.PeriodStart).FirstOrDefaultAsync(ct);

            detail = new RetroDetail(
                view,
                answers,
                snapshot ?? computeFor!,
                snapshot is not null,
                revisit.Select(a => ToView(a, followUps)).ToList(),
                own.Select(a => ToView(a, followUps)).ToList(),
                related.Select(r => ToView(r, 0)).ToList(),
                previous is null ? null : ToView(previous, 0));
        }

        if (computeFor is not null)
        {
            // Live statistics for a draft (or a completed retro that has no snapshot).
            detail = detail with { Stats = await stats.ComputeAsync(detail.Retro.Type, detail.Retro.PeriodStart, ct) };
        }

        return detail;
    }

    /// <summary>Saves a draft: guided answers (blank removes an answer), summary and optional well-being values.</summary>
    public async Task SaveAsync(
        Guid id, IReadOnlyDictionary<string, string?> answers, string? summary, WellbeingInput wellbeing, CancellationToken ct = default)
    {
        foreach (var value in new[] { wellbeing.Energy, wellbeing.Balance, wellbeing.Stress, wellbeing.Satisfaction })
        {
            if (value is < 1 or > 5)
            {
                throw new PlanningException(PlanningException.Codes.InvalidWellbeing);
            }
        }

        await using var db = dbFactory.Create();
        var retro = await Load(db, id, ct);
        if (retro.Status == RetroStatus.Completed)
        {
            throw new PlanningException(PlanningException.Codes.RetroCompleted);
        }

        foreach (var key in answers.Keys)
        {
            if (!RetroQuestions.IsValid(retro.Type, key))
            {
                throw new PlanningException(PlanningException.Codes.InvalidQuestion);
            }
        }

        var existing = await db.RetroResponses.Where(r => r.RetroId == id).ToListAsync(ct);
        foreach (var (key, raw) in answers)
        {
            var text = raw?.Trim();
            var current = existing.FirstOrDefault(r => r.QuestionKey == key);
            if (string.IsNullOrEmpty(text))
            {
                if (current is not null)
                {
                    db.RetroResponses.Remove(current);
                }
            }
            else if (current is null)
            {
                db.RetroResponses.Add(new RetroResponse { RetroId = id, QuestionKey = key, Answer = text });
            }
            else
            {
                current.Answer = text;
            }
        }

        retro.Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        retro.Energy = wellbeing.Energy;
        retro.Balance = wellbeing.Balance;
        retro.Stress = wellbeing.Stress;
        retro.Satisfaction = wellbeing.Satisfaction;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Completes the retro and stores a snapshot of the statistics as they are now.</summary>
    public async Task CompleteAsync(Guid id, CancellationToken ct = default)
    {
        Retrospective retro;
        await using (var db = dbFactory.Create())
        {
            retro = await Load(db, id, ct);
            if (retro.Status == RetroStatus.Completed)
            {
                return;
            }
        }

        var snapshot = await stats.ComputeAsync(retro.Type, retro.PeriodStart, ct);

        await using var db2 = dbFactory.Create();
        var target = await Load(db2, id, ct);
        target.Status = RetroStatus.Completed;
        target.CompletedUtc = clock.UtcNow;
        target.StatsSnapshotJson = JsonSerializer.Serialize(snapshot, Json);
        await db2.SaveChangesAsync(ct);
    }

    public async Task ReopenAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var retro = await Load(db, id, ct);
        retro.Status = RetroStatus.Draft;
        retro.CompletedUtc = null;
        retro.StatsSnapshotJson = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes a draft with its answers. Completed retros must be reopened first. Actions decided in it are kept.</summary>
    public async Task DeleteDraftAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var retro = await Load(db, id, ct);
        if (retro.Status == RetroStatus.Completed)
        {
            throw new PlanningException(PlanningException.Codes.RetroCompleted);
        }

        db.RetroResponses.RemoveRange(await db.RetroResponses.Where(r => r.RetroId == id).ToListAsync(ct));
        foreach (var action in await db.RetroActions.Where(a => a.RetroId == id).ToListAsync(ct))
        {
            action.RetroId = null;
        }

        await db.SaveChangesAsync(ct);
        db.Retrospectives.Remove(retro);
        await db.SaveChangesAsync(ct);
    }

    // ----- actions and experiments -----

    public async Task<Guid> AddActionAsync(Guid? retroId, RetroActionInput input, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        if (retroId is { } rid)
        {
            await Load(db, rid, ct);
        }

        var action = new RetroAction { RetroId = retroId };
        Apply(action, input);
        db.RetroActions.Add(action);
        await db.SaveChangesAsync(ct);
        return action.Id;
    }

    public async Task UpdateActionAsync(Guid actionId, RetroActionInput input, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var action = await LoadAction(db, actionId, ct);
        Apply(action, input);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetActionStatusAsync(Guid actionId, RetroActionStatus status, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var action = await LoadAction(db, actionId, ct);
        action.Status = status;
        action.ClosedUtc = status is RetroActionStatus.Done or RetroActionStatus.Dropped ? clock.UtcNow : null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Records what happened when an action was revisited (optionally during a retro). The history is appended, never edited.</summary>
    public async Task RecordFollowUpAsync(Guid actionId, Guid? retroId, RetroOutcome outcome, string? note, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        await LoadAction(db, actionId, ct);
        db.RetroActionFollowUps.Add(new RetroActionFollowUp
        {
            ActionId = actionId,
            RetroId = retroId,
            RecordedUtc = clock.UtcNow,
            Outcome = outcome,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RetroActionView>> ListOpenActionsAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var actions = await db.RetroActions
            .Where(a => a.Status == RetroActionStatus.Open || a.Status == RetroActionStatus.InProgress)
            .OrderBy(a => a.DueDate == null).ThenBy(a => a.DueDate).ThenBy(a => a.CreatedUtc)
            .ToListAsync(ct);
        var ids = actions.Select(a => a.Id).ToList();
        var followUps = await db.RetroActionFollowUps.Where(f => ids.Contains(f.ActionId)).OrderBy(f => f.RecordedUtc).ToListAsync(ct);
        return actions.Select(a => ToView(a, followUps)).ToList();
    }

    /// <summary>Turns an action into a backlog task (once) so it can be planned like any other work.</summary>
    public async Task<Guid> CreateTaskForActionAsync(Guid actionId, CancellationToken ct = default)
    {
        RetroAction action;
        await using (var db = dbFactory.Create())
        {
            action = await LoadAction(db, actionId, ct);
            if (action.TaskItemId is { } existing)
            {
                return existing;
            }
        }

        var taskId = await tasks.CreateAsync(new TaskInput(action.Title, action.Details, DueDate: action.DueDate), ct: ct);

        await using var db2 = dbFactory.Create();
        var target = await LoadAction(db2, actionId, ct);
        target.TaskItemId = taskId;
        await db2.SaveChangesAsync(ct);
        return taskId;
    }

    private RetroView ToView(Retrospective r, int answerCount) => new(
        r.Id, r.Type, r.PeriodStart, RetroPeriods.EndExclusive(r.Type, r.PeriodStart), r.Status, r.CompletedUtc, r.Summary,
        r.Energy, r.Balance, r.Stress, r.Satisfaction, answerCount, r.UpdatedUtc);

    private static RetroActionView ToView(RetroAction a, IReadOnlyList<RetroActionFollowUp> followUps) => new(
        a.Id, a.RetroId, a.Kind, a.Title, a.Details, a.OwnerName, a.StartDate, a.DueDate, a.Hypothesis, a.Status, a.TaskItemId,
        followUps.Where(f => f.ActionId == a.Id).Select(f => new RetroFollowUpView(f.Id, f.RetroId, f.RecordedUtc, f.Outcome, f.Note)).ToList());

    private static void Apply(RetroAction action, RetroActionInput input)
    {
        var title = input.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            throw new PlanningException(PlanningException.Codes.TitleRequired);
        }

        action.Title = title.Length > 200 ? title[..200] : title;
        action.Kind = input.Kind;
        action.Details = Clean(input.Details);
        action.OwnerName = Clean(input.OwnerName);
        action.StartDate = input.StartDate;
        action.DueDate = input.DueDate;
        action.Hypothesis = input.Kind == RetroActionKind.Experiment ? Clean(input.Hypothesis) : null;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static async Task<Retrospective> Load(IAppDbContext db, Guid id, CancellationToken ct) =>
        await db.Retrospectives.FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new PlanningException(PlanningException.Codes.NotFound);

    private static async Task<RetroAction> LoadAction(IAppDbContext db, Guid id, CancellationToken ct) =>
        await db.RetroActions.FirstOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new PlanningException(PlanningException.Codes.NotFound);
}
