using LifeOS.Application.Abstractions;
using LifeOS.Application.Planning;
using LifeOS.Domain.Ai;
using LifeOS.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Ai;

public sealed record NotificationPreferenceView(
    bool TasksEnabled,
    bool GoalsEnabled,
    bool RetrosEnabled,
    bool HabitsEnabled,
    TimeOnly SendTime,
    TimeOnly QuietStart,
    TimeOnly QuietEnd,
    bool NtfyEnabled,
    bool HomeAssistantEnabled = false);

/// <summary>The owner's settings: AI, notifications and time zone. Everything defaults to off or neutral.</summary>
public sealed class PreferenceService(IAppDbContextFactory dbFactory, IUserProfileStore profile)
{
    public async Task<AiPreferenceView> GetAiAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var pref = await db.UserPreferences.FirstOrDefaultAsync(ct);
        return new AiPreferenceView(pref?.AiEnabled ?? false, pref?.AiMode ?? AiCoachMode.Balanced);
    }

    public async Task SetAiAsync(bool enabled, AiCoachMode mode, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var pref = await db.UserPreferences.FirstOrDefaultAsync(ct);
        if (pref is null)
        {
            pref = new UserPreference();
            db.UserPreferences.Add(pref);
        }

        pref.AiEnabled = enabled;
        pref.AiMode = mode;
        await db.SaveChangesAsync(ct);
    }

    public async Task<NotificationPreferenceView> GetNotificationsAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var p = await db.NotificationPreferences.FirstOrDefaultAsync(ct) ?? new NotificationPreference();
        return ToView(p);
    }

    public async Task SetNotificationsAsync(NotificationPreferenceView view, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var p = await db.NotificationPreferences.FirstOrDefaultAsync(ct);
        if (p is null)
        {
            p = new NotificationPreference();
            db.NotificationPreferences.Add(p);
        }

        p.TasksEnabled = view.TasksEnabled;
        p.GoalsEnabled = view.GoalsEnabled;
        p.RetrosEnabled = view.RetrosEnabled;
        p.HabitsEnabled = view.HabitsEnabled;
        p.SendTime = view.SendTime;
        p.QuietStart = view.QuietStart;
        p.QuietEnd = view.QuietEnd;
        p.NtfyEnabled = view.NtfyEnabled;
        p.HomeAssistantEnabled = view.HomeAssistantEnabled;
        await db.SaveChangesAsync(ct);
    }

    public Task<string> GetTimeZoneAsync(CancellationToken ct = default) => profile.GetTimeZoneIdAsync(ct);

    public async Task SetTimeZoneAsync(string zoneId, CancellationToken ct = default)
    {
        if (!await profile.SetTimeZoneIdAsync(zoneId, ct))
        {
            throw new PlanningException(PlanningException.Codes.InvalidTimeZone);
        }
    }

    internal static NotificationPreferenceView ToView(NotificationPreference p) => new(
        p.TasksEnabled, p.GoalsEnabled, p.RetrosEnabled, p.HabitsEnabled, p.SendTime, p.QuietStart, p.QuietEnd, p.NtfyEnabled, p.HomeAssistantEnabled);
}
