using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOS.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LifeOS.Infrastructure.Reminders;

public sealed class HomeAssistantApiOptions
{
    public const string SectionName = "LifeOS:HomeAssistant";

    /// <summary>Home Assistant's API. Inside a Home Assistant app this is the Supervisor proxy.</summary>
    public string Url { get; set; } = "http://supervisor/core/api/";

    /// <summary>Access token. In a Home Assistant app it is supplied by the Supervisor (SUPERVISOR_TOKEN); never stored in the repository.</summary>
    public string? Token { get; set; }

    /// <summary>The to-do entity that holds the reminders, for example todo.name_todo. Empty means the connection is off.</summary>
    public string? ReminderEntity { get; set; }

    /// <summary>A notify service of the phone running the Home Assistant app, for example notify.mobile_app_name. Empty means off.</summary>
    public string? NotifyService { get; set; }

    public int TimeoutSeconds { get; set; } = 20;
}

/// <summary>Reads and writes one Home Assistant to-do list (for example iCloud Reminders) with the standard todo services.</summary>
public sealed class HomeAssistantReminderClient(
    HttpClient http,
    IOptions<HomeAssistantApiOptions> options,
    ILogger<HomeAssistantReminderClient> logger) : IReminderClient
{
    private string? Token => string.IsNullOrWhiteSpace(options.Value.Token)
        ? Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN")
        : options.Value.Token;

    private string? Entity => string.IsNullOrWhiteSpace(options.Value.ReminderEntity) ? null : options.Value.ReminderEntity.Trim();

    public bool IsConfigured => Entity is not null && !string.IsNullOrWhiteSpace(Token);

    public string? ListName => IsConfigured ? Entity : null;

    public async Task<IReadOnlyList<ReminderItem>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await PostAsync(
            "services/todo/get_items?return_response",
            new { entity_id = Entity, status = new[] { "needs_action", "completed" } },
            expectBody: true,
            cancellationToken);

        var items = new List<ReminderItem>();
        if (doc is null
            || !doc.RootElement.TryGetProperty("service_response", out var response)
            || !response.TryGetProperty(Entity!, out var list)
            || !list.TryGetProperty("items", out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            throw new ReminderException("Unexpected answer from Home Assistant.");
        }

        foreach (var e in array.EnumerateArray())
        {
            var uid = e.TryGetProperty("uid", out var u) ? u.GetString() : null;
            var summary = e.TryGetProperty("summary", out var s) ? s.GetString() : null;
            if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(summary))
            {
                continue;
            }

            var done = e.TryGetProperty("status", out var st) && st.GetString() == "completed";
            DateOnly? due = null;
            DateTimeOffset? dueAt = null;
            if (e.TryGetProperty("due", out var d) && d.ValueKind == JsonValueKind.String && d.GetString() is { Length: > 0 } text)
            {
                if (text.Length == 10 && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    due = date;
                }
                else if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                {
                    dueAt = at;
                }
            }

            items.Add(new ReminderItem(uid, summary.Trim(), done, due, dueAt));
        }

        return items;
    }

    public async Task AddAsync(string summary, DateOnly? due, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["entity_id"] = Entity, ["item"] = summary };
        if (due is { } d)
        {
            body["due_date"] = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        (await PostAsync("services/todo/add_item", body, expectBody: false, cancellationToken))?.Dispose();
    }

    public async Task UpdateAsync(string uid, string? summary, DateOnly? due, bool? done, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["entity_id"] = Entity, ["item"] = uid };
        if (summary is not null)
        {
            body["rename"] = summary;
        }

        if (due is { } d)
        {
            body["due_date"] = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (done is { } flag)
        {
            body["status"] = flag ? "completed" : "needs_action";
        }

        (await PostAsync("services/todo/update_item", body, expectBody: false, cancellationToken))?.Dispose();
    }

    private async Task<JsonDocument?> PostAsync(string path, object body, bool expectBody, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new ReminderException("Reminders are not configured.");
        }

        var baseUrl = options.Value.Url.EndsWith('/') ? options.Value.Url : options.Value.Url + "/";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), path)) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Only the status code is logged: the answer may contain list contents.
                logger.LogWarning("Home Assistant answered {StatusCode} for a reminders call", (int)response.StatusCode);
                throw new ReminderException($"Home Assistant answered {(int)response.StatusCode}.");
            }

            return expectBody ? await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Home Assistant call failed: {Type}", ex.GetType().Name);
            throw new ReminderException("Home Assistant could not be reached.");
        }
    }
}
