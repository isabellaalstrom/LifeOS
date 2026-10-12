namespace LifeOS.Application.Abstractions;

/// <summary>The authenticated owner of the current request/circuit. UserId is null when anonymous.</summary>
public interface ICurrentUser
{
    string? UserId { get; }

    bool IsAuthenticated => UserId is not null;
}
