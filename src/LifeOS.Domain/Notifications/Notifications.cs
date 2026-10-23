using LifeOS.Domain.Common;

namespace LifeOS.Domain.Notifications;

public enum NotificationCategory
{
    Tasks,
    Goals,
    Retros,

    /// <summary>Optional, not scheduled yet.</summary>
    Habits,
}

public enum NotificationStatus
{
    Pending,

    /// <summary>Claimed by the worker; reset to Pending if the worker dies (see LockedUntilUtc).</summary>
    Sending,
    Sent,

    /// <summary>All attempts used up.</summary>
    Failed,

    /// <summary>Intentionally not sent (e.g. the category was switched off, or there is nowhere to send it).</summary>
    Suppressed,
    Cancelled,
}

public enum NotificationChannel
{
    WebPush,
    Ntfy,

    /// <summary>The Home Assistant companion app on the owner's phone, through a notify service.</summary>
    HomeAssistant,
}

public enum DeliveryStatus
{
    Sent,
    Failed,
}

/// <summary>
/// Notification settings. Nothing is sent until a category is switched on. Quiet hours are in the owner's local time:
/// a notification that falls inside them is moved to when they end.
/// </summary>
public class NotificationPreference : OwnedEntity
{
    public bool TasksEnabled { get; set; }

    public bool GoalsEnabled { get; set; }

    public bool RetrosEnabled { get; set; }

    public bool HabitsEnabled { get; set; }

    /// <summary>Local time of day for the daily/weekly reminders.</summary>
    public TimeOnly SendTime { get; set; } = new(8, 0);

    public TimeOnly QuietStart { get; set; } = new(22, 0);

    public TimeOnly QuietEnd { get; set; } = new(7, 0);

    /// <summary>Also deliver through ntfy, when it is configured on the server.</summary>
    public bool NtfyEnabled { get; set; }

    public bool HomeAssistantEnabled { get; set; }

    public bool IsEnabled(NotificationCategory category) => category switch
    {
        NotificationCategory.Tasks => TasksEnabled,
        NotificationCategory.Goals => GoalsEnabled,
        NotificationCategory.Retros => RetrosEnabled,
        NotificationCategory.Habits => HabitsEnabled,
        _ => false,
    };
}

public class ScheduledNotification : OwnedEntity
{
    public NotificationCategory Category { get; set; }

    /// <summary>Stable identity of "this reminder for this period". Unique per owner: the same reminder is never scheduled or sent twice.</summary>
    public string DedupeKey { get; set; } = string.Empty;

    /// <summary>Always generic. Never contains retro answers, summaries or other private reflections.</summary>
    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Where a click leads, relative to the app root.</summary>
    public string Url { get; set; } = string.Empty;

    public DateTimeOffset ScheduledForUtc { get; set; }

    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptUtc { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }

    public DateTimeOffset? SentUtc { get; set; }
}

public class NotificationDelivery : OwnedEntity
{
    public Guid NotificationId { get; set; }

    public NotificationChannel Channel { get; set; }

    public DeliveryStatus Status { get; set; }

    public DateTimeOffset AttemptedUtc { get; set; }

    public string? Error { get; set; }
}

/// <summary>A browser/device registered for Web Push.</summary>
public class PushSubscription : OwnedEntity
{
    public string Endpoint { get; set; } = string.Empty;

    public string P256dh { get; set; } = string.Empty;

    public string Auth { get; set; } = string.Empty;

    public string? UserAgent { get; set; }

    public DateTimeOffset? LastSuccessUtc { get; set; }

    /// <summary>False once the push service reports the subscription gone (404/410).</summary>
    public bool IsActive { get; set; } = true;
}

public static class QuietHours
{
    /// <summary>True when <paramref name="local"/> falls inside the quiet window. A window that wraps midnight (22:00-07:00) is supported; equal start and end means no quiet hours.</summary>
    public static bool IsQuiet(TimeOnly local, TimeOnly start, TimeOnly end)
    {
        if (start == end)
        {
            return false;
        }

        return start < end
            ? local >= start && local < end
            : local >= start || local < end;
    }

    /// <summary>The first moment at or after <paramref name="local"/> that is not quiet.</summary>
    public static DateTime NextAllowed(DateTime local, TimeOnly start, TimeOnly end)
    {
        var time = TimeOnly.FromDateTime(local);
        if (!IsQuiet(time, start, end))
        {
            return local;
        }

        var endToday = local.Date + end.ToTimeSpan();
        // After midnight inside a wrapping window the end is later today; before midnight it is tomorrow.
        return endToday > local ? endToday : endToday.AddDays(1);
    }
}

public static class NotificationRetryPolicy
{
    public const int MaxAttempts = 4;

    /// <summary>Time to wait after the given failed attempt (1-based), or null when no attempts remain.</summary>
    public static TimeSpan? DelayAfterFailure(int attemptsMade) => attemptsMade switch
    {
        1 => TimeSpan.FromMinutes(5),
        2 => TimeSpan.FromMinutes(30),
        3 => TimeSpan.FromHours(2),
        _ => null,
    };
}
