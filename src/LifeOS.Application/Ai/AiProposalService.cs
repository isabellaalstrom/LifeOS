using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOS.Application.Abstractions;
using LifeOS.Application.Goals;
using LifeOS.Application.Planning;
using LifeOS.Application.Retros;
using LifeOS.Domain.Ai;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Retros;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Ai;

public sealed record PlanTaskPayload(Guid TaskId, DateOnly WeekStart);

public sealed record RetroActionPayload(Guid RetroId, string Title, RetroActionKind Kind, string? Details, string? Hypothesis);

public sealed record GoalPayload(
    string Title,
    string? Description,
    GoalHorizon Horizon,
    DateOnly PeriodStart,
    GoalProgressMode Mode,
    decimal? Start,
    decimal? Target,
    string? Unit,
    List<string>? Milestones);

/// <summary>
/// Stores AI suggestions as proposals and applies them only when the owner accepts. Applying goes through the same
/// application services as any manual action, so every rule is validated again. The AI itself has no write access.
/// </summary>
public sealed class AiProposalService(
    IAppDbContextFactory dbFactory,
    IUserClock clock,
    SprintService sprints,
    RetroService retros,
    GoalService goals)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public Task<Guid> CreatePlanTaskAsync(PlanTaskPayload payload, string summary, string? rationale, CancellationToken ct = default) =>
        CreateAsync(AiProposalKind.PlanTask, payload, summary, rationale, ProposalContext.Sprint(payload.WeekStart), ct);

    public Task<Guid> CreateRetroActionAsync(RetroActionPayload payload, string summary, string? rationale, CancellationToken ct = default) =>
        CreateAsync(AiProposalKind.CreateRetroAction, payload, summary, rationale, ProposalContext.Retro(payload.RetroId), ct);

    public Task<Guid> CreateGoalAsync(GoalPayload payload, string summary, string? rationale, string context, CancellationToken ct = default) =>
        CreateAsync(AiProposalKind.CreateGoal, payload, summary, rationale, context, ct);

    public async Task<IReadOnlyList<ProposalView>> ListAsync(string context, bool pendingOnly = true, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var query = db.AiProposals.Where(p => p.Context == context);
        if (pendingOnly)
        {
            query = query.Where(p => p.Status == AiProposalStatus.Pending);
        }

        return await query.OrderBy(p => p.CreatedUtc)
            .Select(p => new ProposalView(p.Id, p.Kind, p.Status, p.Summary, p.Rationale, p.Context, p.LastError))
            .ToListAsync(ct);
    }

    public async Task<int> CountPendingAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        return await db.AiProposals.CountAsync(p => p.Status == AiProposalStatus.Pending, ct);
    }

    /// <summary>Applies a pending proposal through the normal services. If that fails (the data changed meanwhile), it stays pending with the reason.</summary>
    public async Task AcceptAsync(Guid id, CancellationToken ct = default)
    {
        AiProposal proposal;
        await using (var db = dbFactory.Create())
        {
            proposal = await Load(db, id, ct);
            if (proposal.Status != AiProposalStatus.Pending)
            {
                throw new PlanningException(PlanningException.Codes.ProposalDecided);
            }
        }

        Guid? applied;
        try
        {
            applied = await ApplyAsync(proposal, ct);
        }
        catch (PlanningException ex)
        {
            await RecordFailureAsync(id, ex.Code, ct);
            throw;
        }
        catch (JsonException)
        {
            await RecordFailureAsync(id, PlanningException.Codes.InvalidProposal, ct);
            throw new PlanningException(PlanningException.Codes.InvalidProposal);
        }

        await using var db2 = dbFactory.Create();
        var target = await Load(db2, id, ct);
        target.Status = AiProposalStatus.Accepted;
        target.DecidedUtc = clock.UtcNow;
        target.AppliedEntityId = applied;
        target.LastError = null;
        await db2.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var proposal = await Load(db, id, ct);
        if (proposal.Status != AiProposalStatus.Pending)
        {
            throw new PlanningException(PlanningException.Codes.ProposalDecided);
        }

        proposal.Status = AiProposalStatus.Rejected;
        proposal.DecidedUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<Guid?> ApplyAsync(AiProposal proposal, CancellationToken ct)
    {
        switch (proposal.Kind)
        {
            case AiProposalKind.PlanTask:
            {
                var p = Parse<PlanTaskPayload>(proposal);
                await sprints.AddTaskAsync(p.WeekStart, p.TaskId, ct);
                return p.TaskId;
            }

            case AiProposalKind.CreateRetroAction:
            {
                var p = Parse<RetroActionPayload>(proposal);
                return await retros.AddActionAsync(p.RetroId, new RetroActionInput(p.Title, p.Kind, p.Details, null, null, null, p.Hypothesis), ct);
            }

            case AiProposalKind.CreateGoal:
            {
                var p = Parse<GoalPayload>(proposal);
                var id = await goals.CreateAsync(new GoalInput(
                    p.Title, p.Horizon, p.PeriodStart, p.Mode, p.Description, null, null, p.Start, p.Target, null, p.Unit), ct);
                foreach (var milestone in (p.Milestones ?? []).Take(8))
                {
                    await goals.AddMilestoneAsync(id, milestone, null, ct);
                }

                return id;
            }

            default:
                throw new PlanningException(PlanningException.Codes.InvalidProposal);
        }
    }

    private static T Parse<T>(AiProposal proposal) =>
        JsonSerializer.Deserialize<T>(proposal.PayloadJson, Json) ?? throw new PlanningException(PlanningException.Codes.InvalidProposal);

    private async Task<Guid> CreateAsync(
        AiProposalKind kind, object payload, string summary, string? rationale, string context, CancellationToken ct)
    {
        summary = Truncate(summary, 300) ?? throw new PlanningException(PlanningException.Codes.InvalidProposal);
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new PlanningException(PlanningException.Codes.InvalidProposal);
        }

        await using var db = dbFactory.Create();
        var proposal = new AiProposal
        {
            Kind = kind,
            Summary = summary,
            Rationale = Truncate(rationale, 1000),
            Context = context,
            PayloadJson = JsonSerializer.Serialize(payload, payload.GetType(), Json),
        };
        db.AiProposals.Add(proposal);
        await db.SaveChangesAsync(ct);
        return proposal.Id;
    }

    private async Task RecordFailureAsync(Guid id, string reason, CancellationToken ct)
    {
        await using var db = dbFactory.Create();
        var proposal = await Load(db, id, ct);
        proposal.LastError = reason;
        await db.SaveChangesAsync(ct);
    }

    internal static string? Truncate(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        return text.Length > max ? text[..max] : text;
    }

    private static async Task<AiProposal> Load(IAppDbContext db, Guid id, CancellationToken ct) =>
        await db.AiProposals.FirstOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new PlanningException(PlanningException.Codes.NotFound);
}
