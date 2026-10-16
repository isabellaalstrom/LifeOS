using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOS.Application.Abstractions;
using LifeOS.Domain.Notifications;
using LifeOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPush;

namespace LifeOS.Infrastructure.Notifications;

public sealed class WebPushOptions
{
    public const string SectionName = "LifeOS:Notifications:WebPush";

    /// <summary>VAPID subject: a mailto: or https: contact. Defaults to the project page; set your own if you prefer.</summary>
    public string Subject { get; set; } = "https://github.com/isabellaalstrom/LifeOS";
}

public sealed class NtfyOptions
{
    public const string SectionName = "LifeOS:Notifications:Ntfy";

    public string? Url { get; set; }

    public string? Topic { get; set; }

    /// <summary>Optional access token for a protected topic. Never stored in the repository.</summary>
    public string? Token { get; set; }
}

/// <summary>
/// VAPID keys identify this server to the push services. They are generated on first use and kept in the data folder
/// (next to the database), so they survive restarts and are included in backups. The private key never leaves the server.
/// </summary>
public sealed class VapidKeyStore(IOptions<LifeOSOptions> options, ILogger<VapidKeyStore> logger)
{
    private readonly object _gate = new();
    private (string Public, string Private)? _keys;

    private string FilePath => Path.Combine(options.Value.DataPath, "vapid.json");

    public string PublicKey => Get().Public;

    public string PrivateKey => Get().Private;

    private (string Public, string Private) Get()
    {
        lock (_gate)
        {
            if (_keys is { } cached)
            {
                return cached;
            }

            if (File.Exists(FilePath))
            {
                var stored = JsonSerializer.Deserialize<StoredKeys>(File.ReadAllText(FilePath));
                if (stored is { PublicKey.Length: > 0, PrivateKey.Length: > 0 })
                {
                    return (_keys = (stored.PublicKey, stored.PrivateKey)).Value;
                }
            }

            var generated = VapidHelper.GenerateVapidKeys();
            Directory.CreateDirectory(options.Value.DataPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new StoredKeys(generated.PublicKey, generated.PrivateKey)));
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Could not restrict permissions on the VAPID key file");
            }

            logger.LogInformation("Generated new VAPID keys");
            return (_keys = (generated.PublicKey, generated.PrivateKey)).Value;
        }
    }

    private sealed record StoredKeys(string PublicKey, string PrivateKey);
}

/// <summary>Web Push to the owner's registered browsers/devices (the primary channel). Needs HTTPS where the app is opened.</summary>
public sealed class WebPushSender(
    IAppDbContextFactory dbFactory,
    VapidKeyStore keys,
    IOptions<WebPushOptions> options,
    ILogger<WebPushSender> logger) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.WebPush;

    public bool IsAvailable => true;

    public async Task<SendOutcome> SendAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        await using var db = dbFactory.Create();
        var subscriptions = await db.PushSubscriptions.Where(s => s.IsActive).ToListAsync(cancellationToken);
        if (subscriptions.Count == 0)
        {
            return SendOutcome.NoRecipient();
        }

        var vapid = new VapidDetails(options.Value.Subject, keys.PublicKey, keys.PrivateKey);
        var client = new WebPushClient();
        var body = JsonSerializer.Serialize(new { title = payload.Title, body = payload.Body, url = payload.Url });

        var delivered = 0;
        string? lastError = null;
        foreach (var s in subscriptions)
        {
            try
            {
                await client.SendNotificationAsync(new WebPush.PushSubscription(s.Endpoint, s.P256dh, s.Auth), body, vapid, cancellationToken);
                s.LastSuccessUtc = DateTimeOffset.UtcNow;
                delivered++;
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                // The browser unsubscribed: stop trying this one.
                s.IsActive = false;
                lastError = $"subscription gone ({(int)ex.StatusCode})";
            }
            catch (WebPushException ex)
            {
                lastError = $"push service answered {(int)ex.StatusCode}";
                logger.LogWarning("Web Push to a device failed with {StatusCode}", (int)ex.StatusCode);
            }
            catch (HttpRequestException ex)
            {
                lastError = "push service unreachable";
                logger.LogWarning("Web Push to a device failed: {Type}", ex.GetType().Name);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return delivered > 0 ? SendOutcome.Ok() : lastError is not null && subscriptions.All(s => !s.IsActive)
            ? SendOutcome.NoRecipient()
            : SendOutcome.Fail(lastError ?? "no device accepted the message");
    }
}

/// <summary>ntfy as an optional extra channel. Uses ntfy's JSON publishing so Swedish characters are safe.</summary>
public sealed class NtfySender(HttpClient http, IOptions<NtfyOptions> options, ILogger<NtfySender> logger) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Ntfy;

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(options.Value.Url) && !string.IsNullOrWhiteSpace(options.Value.Topic);

    public async Task<SendOutcome> SendAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return SendOutcome.NoRecipient();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.Url)
        {
            Content = JsonContent.Create(new { topic = options.Value.Topic, title = payload.Title, message = payload.Body, priority = 3 }),
        };
        if (!string.IsNullOrWhiteSpace(options.Value.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token);
        }

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return SendOutcome.Ok();
            }

            logger.LogWarning("ntfy answered {StatusCode}", (int)response.StatusCode);
            return SendOutcome.Fail($"ntfy answered {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("ntfy call failed: {Type}", ex.GetType().Name);
            return SendOutcome.Fail("ntfy unreachable");
        }
    }
}
