using System.Net.Http.Headers;
using System.Net.Http.Json;
using LifeOS.Application.Abstractions;
using LifeOS.Domain.Notifications;
using LifeOS.Infrastructure.Reminders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LifeOS.Infrastructure.Notifications;

/// <summary>
/// Sends a notification to the owner's phone through the Home Assistant app, by calling one notify service
/// (for example notify.mobile_app_name). No extra app, account or HTTPS address is needed: Home Assistant already has
/// the phone's push connection. Only the generic notification text is sent.
/// </summary>
public sealed class HomeAssistantNotifySender(
    HttpClient http,
    IOptions<HomeAssistantApiOptions> options,
    ILogger<HomeAssistantNotifySender> logger) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.HomeAssistant;

    private string? Token => string.IsNullOrWhiteSpace(options.Value.Token)
        ? Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN")
        : options.Value.Token;

    /// <summary>The service name after "notify.", or null when missing or not a notify service.</summary>
    private string? ServiceName
    {
        get
        {
            var value = options.Value.NotifyService?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var name = value.StartsWith("notify.", StringComparison.OrdinalIgnoreCase) ? value["notify.".Length..] : value;
            return name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? name : null;
        }
    }

    public bool IsAvailable => ServiceName is not null && !string.IsNullOrWhiteSpace(Token);

    public async Task<SendOutcome> SendAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return SendOutcome.NoRecipient();
        }

        var baseUrl = options.Value.Url.EndsWith('/') ? options.Value.Url : options.Value.Url + "/";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), $"services/notify/{ServiceName}"))
        {
            Content = JsonContent.Create(new { title = payload.Title, message = payload.Body }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return SendOutcome.Ok();
            }

            logger.LogWarning("Home Assistant answered {StatusCode} for a notification", (int)response.StatusCode);
            return SendOutcome.Fail($"Home Assistant answered {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Home Assistant notification failed: {Type}", ex.GetType().Name);
            return SendOutcome.Fail("Home Assistant unreachable");
        }
    }
}
