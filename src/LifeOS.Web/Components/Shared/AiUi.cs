using LifeOS.Application.Ai;
using LifeOS.Domain.Ai;
using Microsoft.Extensions.Localization;

namespace LifeOS.Web.Components.Shared;

/// <summary>Shared texts for the AI features.</summary>
public static class AiUi
{
    /// <summary>A calm explanation for every status that is not Ok. Nothing was changed in any of these cases.</summary>
    public static string StatusText(IStringLocalizer<SharedResource> l, AiStatus status) => status switch
    {
        AiStatus.NotConfigured => l["Ai_Status_NotConfigured"],
        AiStatus.Disabled => l["Ai_Status_Disabled"],
        AiStatus.RateLimited => l["Ai_Status_RateLimited"],
        AiStatus.Failed => l["Ai_Status_Failed"],
        _ => string.Empty,
    };

    public static string ModeLabel(IStringLocalizer<SharedResource> l, AiCoachMode mode) => l["AiMode_" + mode];
}
