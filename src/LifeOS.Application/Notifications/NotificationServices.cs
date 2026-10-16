using LifeOS.Application.Abstractions;
using LifeOS.Application.Planning;
using LifeOS.Application.Retros;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Notifications;
using LifeOS.Domain.Retros;
using LifeOS.Domain.Tasks;
using LifeOS.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Notifications;

/// <summary>Set by background work so the data layer sees the right owner outside of a web request.</summary>
public sealed class OwnerOverride
{
    public string? UserId { get; set; }
}

public sealed record NotificationView(
    Guid Id,
    NotificationCategory Category,
    string Title,
    string Body,
    NotificationStatus Status,
    DateTimeOffset ScheduledForUtc,
    DateTimeOffset? SentUtc,
    int AttemptCount,
    IReadOnlyList<DeliveryView> Deliveries);

public sealed record DeliveryView(NotificationChannel Channel, DeliveryStatus Status, DateTimeOffset AttemptedUtc, string? Error);

public sealed record PushSubscriptionView(Guid Id, string? UserAgent, DateTimeOffset? LastSuccessUtc, bool IsActive);

/// <summary>Registered devices for Web Push.</summary>
public sealed class PushSubscriptionService(IAppDbContextFactory dbFactory)
{
    public async Task SaveAsync(string endpoint, string p256dh, string auth, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(p256dh) || string.IsNullOrWhiteSpace(auth)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new PlanningException(PlanningException.Codes.InvalidSubscription);
        }

        await using var db = dbFactory.Create();
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (existing is null)
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                UserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent,
            });
        }
        else
        {
            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.IsActive = true;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(string endpoint, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (existing is not null)
        {
            db.PushSubscriptions.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<PushSubscriptionView>> ListAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        return await db.PushSubscriptions.OrderBy(s => s.CreatedUtc)
            .Select(s => new PushSubscriptionView(s.Id, s.UserAgent, s.LastSuccessUtc, s.IsActive))
            .ToListAsync(ct);
    }
}

/// <summary>
/// Works out which reminders should exist. Idempotent: every reminder has a stable key ("this reminder for this period"),
/// unique per owner, so running the planner again never creates a second one. Texts are generic by design: counts and
/// invitations only, never titles, answers or any retro content.
/// </summary>
public sealed class NotificationPlanner(IAppDbContextFactory dbFactory, IUserClock clock, RetroService retros)
{
    /// <summary>A reminder that is more than this late is skipped instead of arriving out of the blue.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

    public async Task<int> PlanAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var pref = await db.NotificationPreferences.FirstOrDefaultAsync(ct);
        if (pref is null || !(pref.TasksEnabled || pref.GoalsEnabled || pref.RetrosEnabled))
        {
            return 0;
        }

        var today = clock.Today;
        var candidates = new List<(NotificationCategory Category, string Key, string Title, string Body, string Url, DateOnly Day)>();

        if (pref.TasksEnabled)
        {
            var due = await db.Tasks.CountAsync(
                t => t.ParentTaskId == null && t.DueDate != null && t.DueDate <= today
                     && (t.Status == TaskItemStatus.ToDo || t.Status == TaskItemStatus.InProgress), ct);
            if (due > 0)
            {
                candidates.Add((NotificationCategory.Tasks, $"tasks-due:{today:yyyy-MM-dd}", "Uppgifter att titta på",
                    due == 1 ? "En uppgift förfaller idag eller har passerat sin deadline." : $"{due} uppgifter förfaller idag eller har passerat sin deadline.",
                    "today", today));
            }
        }

        if (pref.GoalsEnabled && today.DayOfWeek == DayOfWeek.Sunday)
        {
            // A gentle weekly nudge when some active goal has not moved for a while. No goal titles.
            var cutoff = clock.UtcNow.AddDays(-14);
            var goals = await db.Goals.Where(g => g.Status == GoalStatus.Active).Select(g => g.Id).ToListAsync(ct);
            var stale = 0;
            foreach (var id in goals)
            {
                var recent = await db.GoalProgressEntries.AnyAsync(e => e.GoalId == id && e.RecordedUtc >= cutoff, ct);
                if (!recent)
                {
                    stale++;
                }
            }

            if (stale > 0)
            {
                candidates.Add((NotificationCategory.Goals, $"goals-checkin:{PeriodCalculator.WeekStart(today):yyyy-MM-dd}", "Läget med målen?",
                    "Det kan vara dags för en liten koll på dina mål.", "goals", today));
            }
        }

        if (pref.RetrosEnabled)
        {
            foreach (var s in await retros.SuggestionsAsync(ct))
            {
                var ended = RetroPeriods.EndExclusive(s.Type, s.PeriodStart);
                if (today >= ended && today <= ended.AddDays(7))
                {
                    var what = s.Type switch { RetroType.Monthly => "förra månaden", RetroType.Quarterly => "förra kvartalet", _ => "förra året" };
                    candidates.Add((NotificationCategory.Retros, $"retro:{s.Type}:{s.PeriodStart:yyyy-MM-dd}", "Dags för en återblick?",
                        $"Vill du se tillbaka på {what}? Det går bra att göra när det passar.", "retros", today));
                }
            }
        }

        var existing = (await db.ScheduledNotifications.Select(n => n.DedupeKey).ToListAsync(ct)).ToHashSet();
        var created = 0;
        var nowUtc = clock.UtcNow;
        foreach (var c in candidates)
        {
            if (existing.Contains(c.Key))
            {
                continue;
            }

            var local = c.Day.ToDateTime(TimeOnly.MinValue) + pref.SendTime.ToTimeSpan();
            local = QuietHours.NextAllowed(local, pref.QuietStart, pref.QuietEnd);
            var whenUtc = new DateTimeOffset(local, clock.Zone.GetUtcOffset(local)).ToUniversalTime();
            if (nowUtc - whenUtc > StaleAfter)
            {
                continue;
            }

            db.ScheduledNotifications.Add(new ScheduledNotification
            {
                Category = c.Category,
                DedupeKey = c.Key,
                Title = c.Title,
                Body = c.Body,
                Url = c.Url,
                ScheduledForUtc = whenUtc,
            });
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return created;
    }
}

/// <summary>
/// Sends what is due. Restart-safe: a notification is claimed by setting it to Sending with a lock time; if the process dies,
/// the lock expires and the next run picks it up again. Failures are retried with growing delays and then given up on.
/// </summary>
public sealed class NotificationDispatcher(
    IAppDbContextFactory dbFactory,
    IUserClock clock,
    IEnumerable<INotificationSender> senders)
{
    private static readonly TimeSpan LockTime = TimeSpan.FromMinutes(5);

    public async Task<int> DispatchDueAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var pref = await db.NotificationPreferences.FirstOrDefaultAsync(ct) ?? new NotificationPreference();
        var now = clock.UtcNow;

        var due = await db.ScheduledNotifications
            .Where(n => (n.Status == NotificationStatus.Pending && n.ScheduledForUtc <= now && (n.NextAttemptUtc == null || n.NextAttemptUtc <= now))
                        || (n.Status == NotificationStatus.Sending && n.LockedUntilUtc != null && n.LockedUntilUtc < now))
            .OrderBy(n => n.ScheduledForUtc)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var n in due)
        {
            // Switched off since it was planned: do not send, and say so.
            if (n.Category != NotificationCategory.Habits && !pref.IsEnabled(n.Category) && !n.DedupeKey.StartsWith("test:", StringComparison.Ordinal))
            {
                n.Status = NotificationStatus.Suppressed;
                n.LockedUntilUtc = null;
                await db.SaveChangesAsync(ct);
                continue;
            }

            // Quiet hours apply at send time as well (a retry may land in them).
            var local = TimeZoneInfo.ConvertTime(now, clock.Zone).DateTime;
            if (!n.DedupeKey.StartsWith("test:", StringComparison.Ordinal) && QuietHours.IsQuiet(TimeOnly.FromDateTime(local), pref.QuietStart, pref.QuietEnd))
            {
                var allowed = QuietHours.NextAllowed(local, pref.QuietStart, pref.QuietEnd);
                n.Status = NotificationStatus.Pending;
                n.ScheduledForUtc = new DateTimeOffset(allowed, clock.Zone.GetUtcOffset(allowed)).ToUniversalTime();
                n.NextAttemptUtc = null;
                n.LockedUntilUtc = null;
                await db.SaveChangesAsync(ct);
                continue;
            }

            n.Status = NotificationStatus.Sending;
            n.LockedUntilUtc = now + LockTime;
            n.AttemptCount++;
            await db.SaveChangesAsync(ct);

            var payload = new NotificationPayload(n.Title, n.Body, n.Url);
            var delivered = false;
            var anyRecipient = false;
            var errors = new List<string>();
            foreach (var sender in senders.Where(s => s.IsAvailable && (s.Channel == NotificationChannel.WebPush || pref.NtfyEnabled)))
            {
                SendOutcome outcome;
                try
                {
                    outcome = await sender.SendAsync(payload, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    outcome = SendOutcome.Fail(ex.GetType().Name);
                }

                if (outcome.IsNoRecipient)
                {
                    continue;
                }

                anyRecipient = true;
                db.NotificationDeliveries.Add(new NotificationDelivery
                {
                    NotificationId = n.Id,
                    Channel = sender.Channel,
                    Status = outcome.Delivered ? DeliveryStatus.Sent : DeliveryStatus.Failed,
                    AttemptedUtc = clock.UtcNow,
                    Error = outcome.Error is { Length: > 300 } e ? e[..300] : outcome.Error,
                });
                delivered |= outcome.Delivered;
                if (!outcome.Delivered && outcome.Error is not null)
                {
                    errors.Add(outcome.Error);
                }
            }

            n.LockedUntilUtc = null;
            if (delivered)
            {
                n.Status = NotificationStatus.Sent;
                n.SentUtc = clock.UtcNow;
                sent++;
            }
            else if (!anyRecipient)
            {
                // Nowhere to send (no device registered): not an error and not worth retrying.
                n.Status = NotificationStatus.Suppressed;
            }
            else if (NotificationRetryPolicy.DelayAfterFailure(n.AttemptCount) is { } delay)
            {
                n.Status = NotificationStatus.Pending;
                n.NextAttemptUtc = clock.UtcNow + delay;
            }
            else
            {
                n.Status = NotificationStatus.Failed;
            }

            await db.SaveChangesAsync(ct);
        }

        return sent;
    }
}

/// <summary>One background pass for the current owner: plan what should exist, then send what is due.</summary>
public sealed class NotificationRunner(NotificationPlanner planner, NotificationDispatcher dispatcher)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        await planner.PlanAsync(ct);
        await dispatcher.DispatchDueAsync(ct);
    }
}

/// <summary>What the settings page needs: recent notifications with their delivery attempts, and a test message.</summary>
public sealed class NotificationService(IAppDbContextFactory dbFactory, IUserClock clock, NotificationRunner runner)
{
    public async Task<IReadOnlyList<NotificationView>> RecentAsync(int take = 15, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var items = await db.ScheduledNotifications.OrderByDescending(n => n.ScheduledForUtc).Take(take).ToListAsync(ct);
        var ids = items.Select(i => i.Id).ToList();
        var deliveries = await db.NotificationDeliveries.Where(d => ids.Contains(d.NotificationId)).OrderBy(d => d.AttemptedUtc).ToListAsync(ct);
        return items.Select(n => new NotificationView(
            n.Id, n.Category, n.Title, n.Body, n.Status, n.ScheduledForUtc, n.SentUtc, n.AttemptCount,
            deliveries.Where(d => d.NotificationId == n.Id).Select(d => new DeliveryView(d.Channel, d.Status, d.AttemptedUtc, d.Error)).ToList())).ToList();
    }

    /// <summary>Sends a harmless test message right away (ignores categories and quiet hours, since the owner asked for it).</summary>
    public async Task SendTestAsync(CancellationToken ct = default)
    {
        await using (var db = dbFactory.Create())
        {
            db.ScheduledNotifications.Add(new ScheduledNotification
            {
                Category = NotificationCategory.Tasks,
                DedupeKey = $"test:{Guid.NewGuid():N}",
                Title = "LifeOS",
                Body = "Det här är en testnotis. Allt fungerar.",
                Url = "",
                ScheduledForUtc = clock.UtcNow.AddSeconds(-1),
            });
            await db.SaveChangesAsync(ct);
        }

        await runner.RunAsync(ct);
    }
}
