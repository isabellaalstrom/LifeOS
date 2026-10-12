using System.Security.Claims;
using LifeOS.Application.Abstractions;
using Microsoft.AspNetCore.Components.Authorization;

namespace LifeOS.Web.Services;

/// <summary>
/// Resolves the owner from the HTTP request (static SSR / endpoints) or the circuit's authentication state.
/// Anonymous resolves to null, which the data layer treats as "no access".
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor, AuthenticationStateProvider authenticationStateProvider)
    : ICurrentUser
{
    public string? UserId
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                principal = TryGetCircuitUser();
            }

            return principal?.Identity?.IsAuthenticated == true
                ? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;
        }
    }

    private ClaimsPrincipal? TryGetCircuitUser()
    {
        try
        {
            var task = authenticationStateProvider.GetAuthenticationStateAsync();
            return task.IsCompletedSuccessfully ? task.Result.User : null;
        }
        catch (InvalidOperationException)
        {
            // No Blazor endpoint context (e.g. minimal API or health check).
            return null;
        }
    }
}
