using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LifeOS.Application.Abstractions;
using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Domain.Ai;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Retros;
using LifeOS.Domain.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LifeOS.Application.Ai;

/// <summary>
/// The coach. It reads a minimal context, asks the provider, and turns the answer into data that is shown to the owner.
/// Anything that could change the app is stored as a Pending proposal, never applied here. If AI is off, unconfigured,
/// rate limited or failing, callers get a status and nothing else happens: the rest of the app does not depend on it.
/// </summary>
public sealed class AiCoachService(
    IAiClient client,
    IAppDbContextFactory dbFactory,
    IUserClock clock,
    PreferenceService preferences,
    AiContextBuilder contexts,
    AiProposalService proposals,
    IOptions<AiOptions> options) : IAiCoachService
{
    private const string BasePrompt =
        "Du är en vänlig, lugn och ärlig coach i appen LifeOS. Svara alltid på svenska med enkelt språk. " +
        "Du ser bara den data som skickas med; hitta aldrig på fakta. " +
        "Skilj tydligt mellan observationer (det datan faktiskt visar) och hypoteser (försiktiga tolkningar, formulerade som möjligheter, aldrig som sanningar). " +
        "Tolka aldrig ofullständiga siffror som misslyckanden och skuldbelägg aldrig. Föreslå små, konkreta saker. " +
        "Du kan inte ändra något själv: allt du föreslår är förslag som personen själv väljer att godkänna. " +
        "Svara ENDAST med giltig JSON enligt schemat, utan extra text.";

    public async Task<AiAvailability> GetAvailabilityAsync(CancellationToken ct = default)
    {
        var pref = await preferences.GetAiAsync(ct);
        return new AiAvailability(client.IsConfigured, pref.Enabled, await CallsTodayAsync(ct), options.Value.DailyLimit);
    }

    public async Task<AiResult<CoachReply>> RetroFollowUpAsync(Guid retroId, AiCoachMode? mode, string? ownerMessage, CancellationToken ct = default)
    {
        var context = await contexts.RetroAsync(retroId, ct) ?? throw new PlanningException(PlanningException.Codes.NotFound);
        var effectiveMode = mode ?? (await preferences.GetAiAsync(ct)).Mode;

        List<AiRetroMessage> history;
        await using (var db = dbFactory.Create())
        {
            history = await db.AiRetroMessages.Where(m => m.RetroId == retroId).OrderBy(m => m.CreatedUtc).Take(20).ToListAsync(ct);
        }

        var messages = new List<AiChatMessage> { new("user", "Underlag (JSON):\n" + context) };
        foreach (var m in history)
        {
            messages.Add(new(m.IsUser ? "user" : "assistant", m.IsUser ? m.Content : m.Content));
        }

        if (!string.IsNullOrWhiteSpace(ownerMessage))
        {
            messages.Add(new("user", AiProposalService.Truncate(ownerMessage, 1500)!));
        }

        const string schema =
            "Schema: {\"observations\":[\"..\"],\"hypotheses\":[\"..\"],\"question\":\"..\"}. " +
            "observations: högst 3 punkter som direkt stöds av underlaget. hypotheses: högst 2 försiktiga tolkningar. " +
            "question: en enda öppen följdfråga som hjälper personen att reflektera vidare.";

        var (status, doc) = await CallAsync(AiFeature.RetroCoach, effectiveMode, schema, messages, 700, ct);
        if (doc is null)
        {
            return AiResult<CoachReply>.Fail(status);
        }

        using (doc)
        {
            var reply = new CoachReply(Strings(doc.RootElement, "observations", 3), Strings(doc.RootElement, "hypotheses", 2), Text(doc.RootElement, "question", 400) ?? string.Empty);
            if (reply.Question.Length == 0 && reply.Observations.Count == 0 && reply.Hypotheses.Count == 0)
            {
                return AiResult<CoachReply>.Fail(AiStatus.Failed);
            }

            await using var db = dbFactory.Create();
            if (!string.IsNullOrWhiteSpace(ownerMessage))
            {
                db.AiRetroMessages.Add(new AiRetroMessage { RetroId = retroId, IsUser = true, Content = AiProposalService.Truncate(ownerMessage, 1500)!, Mode = effectiveMode });
                await db.SaveChangesAsync(ct); // saved first so the conversation keeps its order
            }

            db.AiRetroMessages.Add(new AiRetroMessage
            {
                RetroId = retroId,
                IsUser = false,
                Content = reply.Question,
                Mode = effectiveMode,
                Observations = reply.Observations.Count == 0 ? null : string.Join('\n', reply.Observations),
                Hypotheses = reply.Hypotheses.Count == 0 ? null : string.Join('\n', reply.Hypotheses),
            });
            await db.SaveChangesAsync(ct);
            return AiResult<CoachReply>.Success(reply);
        }
    }

    public async Task<AiResult<IReadOnlyList<Guid>>> ProposeRetroActionsAsync(Guid retroId, AiCoachMode? mode, CancellationToken ct = default)
    {
        var context = await contexts.RetroAsync(retroId, ct) ?? throw new PlanningException(PlanningException.Codes.NotFound);
        var effectiveMode = mode ?? (await preferences.GetAiAsync(ct)).Mode;

        const string schema =
            "Schema: {\"actions\":[{\"title\":\"..\",\"kind\":\"Action\"|\"Experiment\",\"details\":\"..\",\"hypothesis\":\"..\",\"rationale\":\"..\"}]}. " +
            "Föreslå högst 3 små, konkreta åtgärder eller experiment. hypothesis fylls bara i för experiment. rationale förklarar kort varför.";

        var (status, doc) = await CallAsync(AiFeature.RetroCoach, effectiveMode, schema, [new("user", "Underlag (JSON):\n" + context)], 800, ct);
        if (doc is null)
        {
            return AiResult<IReadOnlyList<Guid>>.Fail(status);
        }

        using (doc)
        {
            var ids = new List<Guid>();
            if (doc.RootElement.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in actions.EnumerateArray())
                {
                    if (ids.Count >= 3)
                    {
                        break;
                    }

                    if (a.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var title = Text(a, "title", 200);
                    if (title is null)
                    {
                        continue;
                    }

                    var kind = string.Equals(Text(a, "kind", 20), "Experiment", StringComparison.OrdinalIgnoreCase)
                        ? RetroActionKind.Experiment
                        : RetroActionKind.Action;
                    ids.Add(await proposals.CreateRetroActionAsync(
                        new RetroActionPayload(retroId, title, kind, Text(a, "details", 1000), kind == RetroActionKind.Experiment ? Text(a, "hypothesis", 500) : null),
                        title,
                        Text(a, "rationale", 600),
                        ct));
                }
            }

            return ids.Count == 0 ? AiResult<IReadOnlyList<Guid>>.Fail(AiStatus.Failed) : AiResult<IReadOnlyList<Guid>>.Success(ids);
        }
    }

    public async Task<AiResult<GoalSuggestion>> FormulateGoalAsync(string roughIdea, GoalHorizon horizon, CancellationToken ct = default)
    {
        roughIdea = AiProposalService.Truncate(roughIdea, 600) ?? throw new PlanningException(PlanningException.Codes.TitleRequired);
        var prefs = await preferences.GetAiAsync(ct);

        const string schema =
            "Schema: {\"title\":\"..\",\"description\":\"..\",\"mode\":\"Numeric\"|\"Milestones\"|\"LinkedTasks\"|\"Manual\"," +
            "\"start\":number|null,\"target\":number|null,\"unit\":\"..\"|null,\"milestones\":[\"..\"]}. " +
            "Gör idén till ett tydligt, mätbart mål. Använd Numeric bara om det finns ett naturligt mätvärde (då start, target, unit). " +
            "milestones: högst 5 steg, bara om de behövs.";

        var user = $"Horisont: {horizon}. Personens idé: {roughIdea}";
        var (status, doc) = await CallAsync(AiFeature.GoalFormulation, prefs.Mode, schema, [new("user", user)], 600, ct);
        if (doc is null)
        {
            return AiResult<GoalSuggestion>.Fail(status);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var title = Text(root, "title", 200);
            if (title is null)
            {
                return AiResult<GoalSuggestion>.Fail(AiStatus.Failed);
            }

            var mode = Enum.TryParse<GoalProgressMode>(Text(root, "mode", 20), true, out var m) ? m : GoalProgressMode.Manual;
            var start = Number(root, "start");
            var target = Number(root, "target");
            if (mode == GoalProgressMode.Numeric && (start is null || target is null))
            {
                mode = GoalProgressMode.Manual;
            }

            return AiResult<GoalSuggestion>.Success(new GoalSuggestion(
                title, Text(root, "description", 1000), horizon, mode,
                mode == GoalProgressMode.Numeric ? start : null, mode == GoalProgressMode.Numeric ? target : null,
                mode == GoalProgressMode.Numeric ? Text(root, "unit", 30) : null,
                Strings(root, "milestones", 5)));
        }
    }

    public async Task<AiResult<IReadOnlyList<Guid>>> SuggestSprintAsync(DateOnly anyDateInWeek, CancellationToken ct = default)
    {
        var week = PeriodCalculator.WeekStart(anyDateInWeek);
        var (context, candidates) = await contexts.SprintAsync(week, ct);
        if (candidates.Count == 0)
        {
            return AiResult<IReadOnlyList<Guid>>.Success([]);
        }

        var prefs = await preferences.GetAiAsync(ct);
        const string schema =
            "Schema: {\"picks\":[{\"n\":number,\"reason\":\"..\"}]}. Välj högst 6 uppgifter ur backlogCandidates (använd deras n) som rimligen ryms i veckan " +
            "med hänsyn till kapaciteten. Föreslå hellre färre än fler. reason är en kort motivering. Om kapaciteten redan är nådd: tom lista.";

        var (status, doc) = await CallAsync(AiFeature.SprintSuggestions, prefs.Mode, schema, [new("user", "Underlag (JSON):\n" + context)], 600, ct);
        if (doc is null)
        {
            return AiResult<IReadOnlyList<Guid>>.Fail(status);
        }

        using (doc)
        {
            var ids = new List<Guid>();
            var seen = new HashSet<int>();
            if (doc.RootElement.TryGetProperty("picks", out var picks) && picks.ValueKind == JsonValueKind.Array)
            {
                foreach (var pick in picks.EnumerateArray().Take(6))
                {
                    // Only numbers that really refer to a candidate we sent are accepted; anything else is ignored.
                    if (pick.ValueKind != JsonValueKind.Object || !pick.TryGetProperty("n", out var nElement)
                        || nElement.ValueKind != JsonValueKind.Number || !nElement.TryGetInt32(out var n)
                        || n < 1 || n > candidates.Count || !seen.Add(n))
                    {
                        continue;
                    }

                    var task = candidates[n - 1];
                    ids.Add(await proposals.CreatePlanTaskAsync(new PlanTaskPayload(task.Id, week), task.Title, Text(pick, "reason", 400), ct));
                }
            }

            return AiResult<IReadOnlyList<Guid>>.Success(ids);
        }
    }

    public async Task<AiResult<DashboardInsight>> GenerateInsightAsync(bool force, CancellationToken ct = default)
    {
        var availability = await GetAvailabilityAsync(ct);
        if (!availability.EnabledByOwner)
        {
            return AiResult<DashboardInsight>.Fail(AiStatus.Disabled);
        }

        if (!availability.ProviderConfigured)
        {
            return AiResult<DashboardInsight>.Fail(AiStatus.NotConfigured);
        }

        var context = await contexts.InsightAsync(ct);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context)));
        var now = clock.UtcNow;
        var ttl = TimeSpan.FromHours(options.Value.InsightTtlHours);

        await using (var db = dbFactory.Create())
        {
            var latest = await db.AiInsights.Where(i => i.Feature == AiFeature.DashboardInsight)
                .OrderByDescending(i => i.GeneratedUtc).FirstOrDefaultAsync(ct);
            if (latest is not null)
            {
                var fresh = now - latest.GeneratedUtc < ttl;
                var tooSoonToForce = now - latest.GeneratedUtc < TimeSpan.FromMinutes(5);
                if ((fresh && latest.ContextHash == hash && !force) || (force && tooSoonToForce))
                {
                    return AiResult<DashboardInsight>.Success(ToInsight(latest, fromCache: true));
                }
            }
        }

        var prefs = await preferences.GetAiAsync(ct);
        const string schema =
            "Schema: {\"observations\":[\"..\"],\"hypotheses\":[\"..\"]}. observations: högst 4 punkter som datan direkt visar (jämför gärna månaderna). " +
            "hypotheses: högst 2 försiktiga tolkningar som möjligheter. Inga råd som kräver mer data än du fått.";

        var (status, doc) = await CallAsync(AiFeature.DashboardInsight, prefs.Mode, schema, [new("user", "Underlag (JSON):\n" + context)], 600, ct);
        if (doc is null)
        {
            return AiResult<DashboardInsight>.Fail(status);
        }

        using (doc)
        {
            var observations = Strings(doc.RootElement, "observations", 4);
            var hypotheses = Strings(doc.RootElement, "hypotheses", 2);
            if (observations.Count == 0 && hypotheses.Count == 0)
            {
                return AiResult<DashboardInsight>.Fail(AiStatus.Failed);
            }

            await using var db = dbFactory.Create();
            var insight = new AiInsight
            {
                Feature = AiFeature.DashboardInsight,
                ContextHash = hash,
                Observations = string.Join('\n', observations),
                Hypotheses = string.Join('\n', hypotheses),
                GeneratedUtc = now,
            };
            db.AiInsights.Add(insight);
            await db.SaveChangesAsync(ct);
            return AiResult<DashboardInsight>.Success(ToInsight(insight, fromCache: false));
        }
    }

    public async Task<IReadOnlyList<RetroChatMessage>> GetRetroChatAsync(Guid retroId, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var rows = await db.AiRetroMessages.Where(m => m.RetroId == retroId).OrderBy(m => m.CreatedUtc).ToListAsync(ct);
        return rows.Select(m => new RetroChatMessage(
            m.Id, m.IsUser, m.Content, m.Mode,
            m.Observations is null ? [] : Lines(m.Observations),
            m.Hypotheses is null ? [] : Lines(m.Hypotheses),
            m.CreatedUtc)).ToList();
    }

    public async Task<DashboardInsight?> GetCachedInsightAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var latest = await db.AiInsights.Where(i => i.Feature == AiFeature.DashboardInsight)
            .OrderByDescending(i => i.GeneratedUtc).FirstOrDefaultAsync(ct);
        return latest is null ? null : ToInsight(latest, fromCache: true);
    }

    private async Task<(AiStatus Status, JsonDocument? Doc)> CallAsync(
        AiFeature feature, AiCoachMode mode, string schema, IReadOnlyList<AiChatMessage> messages, int maxTokens, CancellationToken ct)
    {
        var prefs = await preferences.GetAiAsync(ct);
        if (!prefs.Enabled)
        {
            return (AiStatus.Disabled, null);
        }

        if (!client.IsConfigured)
        {
            return (AiStatus.NotConfigured, null);
        }

        if (await CallsTodayAsync(ct) >= options.Value.DailyLimit)
        {
            return (AiStatus.RateLimited, null);
        }

        var system = BasePrompt + " " + ModeInstruction(mode) + " " + schema;
        var completion = await client.CompleteAsync(new AiRequest(system, messages, JsonOutput: true, MaxOutputTokens: maxTokens), ct);

        await LogCallAsync(feature, completion.Ok, completion.Failure.ToString(), ct);

        if (!completion.Ok)
        {
            return (completion.Failure switch
            {
                AiFailure.NotConfigured => AiStatus.NotConfigured,
                AiFailure.RateLimited => AiStatus.RateLimited,
                _ => AiStatus.Failed,
            }, null);
        }

        var doc = TryParse(completion.Text!);
        return doc is null ? (AiStatus.Failed, null) : (AiStatus.Ok, doc);
    }

    private static string ModeInstruction(AiCoachMode mode) => mode switch
    {
        AiCoachMode.Analytical => "Läge: analytisk. Utgå från siffror och mönster, var konkret och kortfattad.",
        AiCoachMode.Reflective => "Läge: reflekterande. Utgå från känslor, mening och värderingar, ställ öppna frågor och använd få siffror.",
        _ => "Läge: balanserad. Blanda konkreta observationer med en varm, öppen fråga.",
    };

    private async Task<int> CallsTodayAsync(CancellationToken ct)
    {
        var (startUtc, endUtc) = PeriodCalculator.ToUtcRange(clock.Today, clock.Today.AddDays(1), clock.Zone);
        await using var db = dbFactory.Create();
        return await db.AiCallLogs.CountAsync(l => l.OccurredUtc >= startUtc && l.OccurredUtc < endUtc, ct);
    }

    private async Task LogCallAsync(AiFeature feature, bool success, string status, CancellationToken ct)
    {
        await using var db = dbFactory.Create();
        db.AiCallLogs.Add(new AiCallLog { Feature = feature, OccurredUtc = clock.UtcNow, Success = success, Status = status });
        await db.SaveChangesAsync(ct);
    }

    private static DashboardInsight ToInsight(AiInsight i, bool fromCache) => new(
        Lines(i.Observations), Lines(i.Hypotheses), i.GeneratedUtc, fromCache);

    private static IReadOnlyList<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static JsonDocument? TryParse(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstBreak > 0 && lastFence > firstBreak)
            {
                text = text[(firstBreak + 1)..lastFence].Trim();
            }
        }

        try
        {
            var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                return doc;
            }

            doc.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name, int max) =>
        element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? AiProposalService.Truncate(p.GetString(), max) : null;

    private static decimal? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d) ? d : null;

    private static IReadOnlyList<string> Strings(JsonElement element, string name, int maxItems)
    {
        if (!element.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return p.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => AiProposalService.Truncate(e.GetString(), 400))
            .Where(s => s is not null)
            .Select(s => s!)
            .Take(maxItems)
            .ToList();
    }
}
