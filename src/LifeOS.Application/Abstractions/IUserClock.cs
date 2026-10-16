namespace LifeOS.Application.Abstractions;

/// <summary>Current time and "today" in the signed-in user's time zone.</summary>
public interface IUserClock
{
    TimeZoneInfo Zone { get; }

    DateTimeOffset UtcNow { get; }

    DateOnly Today { get; }
}
