using LifeOS.Domain.Notifications;

namespace LifeOS.Application.Abstractions;

/// <summary>What is sent. Always generic: never private reflections.</summary>
public sealed record NotificationPayload(string Title, string Body, string Url);

public sealed record SendOutcome(bool Delivered, string? Error = null)
{
    public static SendOutcome Ok() => new(true);

    public static SendOutcome Fail(string error) => new(false, error);

    /// <summary>Nothing to send to (no device registered, channel not configured). Not an error and not retried.</summary>
    public static SendOutcome NoRecipient() => new(false, NoRecipientMarker);

    public const string NoRecipientMarker = "no-recipient";

    public bool IsNoRecipient => !Delivered && Error == NoRecipientMarker;
}

/// <summary>A delivery channel. Web Push first, ntfy optional. Runs inside the owner's scope (it can read that owner's subscriptions).</summary>
public interface INotificationSender
{
    NotificationChannel Channel { get; }

    /// <summary>Whether the channel is set up on this server at all.</summary>
    bool IsAvailable { get; }

    Task<SendOutcome> SendAsync(NotificationPayload payload, CancellationToken cancellationToken = default);
}
