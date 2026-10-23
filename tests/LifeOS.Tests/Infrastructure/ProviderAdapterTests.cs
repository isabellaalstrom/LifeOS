using System.Net;
using System.Text;
using System.Text.Json;
using LifeOS.Application.Abstractions;
using LifeOS.Domain.Notifications;
using LifeOS.Infrastructure.Ai;
using LifeOS.Infrastructure.Notifications;
using LifeOS.Infrastructure.Persistence;
using LifeOS.Infrastructure.Reminders;
using LifeOS.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LifeOS.Tests.Infrastructure;

public class ProviderAdapterTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            return respond(request, body);
        }
    }

    private static OpenAiClient Client(StubHandler handler, string? key = "sk-secret-test-key") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") },
            Options.Create(new OpenAiOptions { ApiKey = key, Model = "test-model" }),
            NullLogger<OpenAiClient>.Instance);

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = text } } } }),
            Encoding.UTF8, "application/json"),
    };

    private static readonly AiRequest Request = new("Systemtext", [new AiChatMessage("user", "Hej")], true, 321);

    [Fact]
    public async Task A_request_goes_to_chat_completions_with_the_key_in_the_header_only()
    {
        var handler = new StubHandler((_, _) => Json("""{"ok":true}"""));

        var result = await Client(handler).CompleteAsync(Request);

        Assert.True(result.Ok);
        Assert.Equal("""{"ok":true}""", result.Text);
        var (req, body) = Assert.Single(handler.Calls);
        Assert.Equal("https://api.example/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer sk-secret-test-key", req.Headers.Authorization!.ToString());
        Assert.DoesNotContain("sk-secret-test-key", body);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("test-model", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal(321, doc.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal("json_object", doc.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(("system", "Systemtext"), (messages[0].GetProperty("role").GetString(), messages[0].GetProperty("content").GetString()));
        Assert.Equal(("user", "Hej"), (messages[1].GetProperty("role").GetString(), messages[1].GetProperty("content").GetString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_a_key_nothing_is_sent(string? key)
    {
        var handler = new StubHandler((_, _) => Json("x"));
        var client = Client(handler, key);

        Assert.False(client.IsConfigured);
        Assert.Equal(AiFailure.NotConfigured, (await client.CompleteAsync(Request)).Failure);
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AiFailure.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, AiFailure.Rejected)]
    [InlineData(HttpStatusCode.TooManyRequests, AiFailure.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, AiFailure.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AiFailure.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, AiFailure.BadResponse)]
    public async Task Error_statuses_map_to_failures(HttpStatusCode status, AiFailure expected)
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(status) { Content = new StringContent("{\"error\":\"detaljer\"}") });

        Assert.Equal(expected, (await Client(handler).CompleteAsync(Request)).Failure);
    }

    [Fact]
    public async Task Network_errors_and_malformed_answers_fail_cleanly()
    {
        var down = new StubHandler((_, _) => throw new HttpRequestException("nere"));
        Assert.Equal(AiFailure.Unavailable, (await Client(down).CompleteAsync(Request)).Failure);

        var junk = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("inte json") });
        Assert.Equal(AiFailure.BadResponse, (await Client(junk).CompleteAsync(Request)).Failure);

        var empty = new StubHandler((_, _) => Json("  "));
        Assert.Equal(AiFailure.BadResponse, (await Client(empty).CompleteAsync(Request)).Failure);

        var noChoices = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[]}", Encoding.UTF8, "application/json") });
        Assert.Equal(AiFailure.BadResponse, (await Client(noChoices).CompleteAsync(Request)).Failure);
    }

    [Fact]
    public async Task The_owners_own_cancellation_is_not_reported_as_a_provider_failure()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHandler((_, _) =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).CompleteAsync(Request, cts.Token));
    }

    // ---------- VAPID keys ----------

    [Fact]
    public void Vapid_keys_are_generated_once_and_survive_a_restart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lifeos-vapid-" + Guid.NewGuid().ToString("N"));
        try
        {
            VapidKeyStore Store() => new(Options.Create(new LifeOSOptions { DataPath = dir }), NullLogger<VapidKeyStore>.Instance);

            var first = Store();
            var publicKey = first.PublicKey;
            Assert.False(string.IsNullOrWhiteSpace(publicKey));
            Assert.NotEqual(publicKey, first.PrivateKey);
            Assert.Equal(publicKey, first.PublicKey);

            var restarted = Store();                       // a new process reads the stored keys instead of making new ones
            Assert.Equal(publicKey, restarted.PublicKey);
            Assert.Equal(first.PrivateKey, restarted.PrivateKey);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    // ---------- Home Assistant options ----------

    [Fact]
    public void Ha_options_map_to_configuration_and_blank_values_are_ignored()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, """
                {"openai_api_key":"sk-abc","openai_model":"  ","ntfy_url":"https://ntfy.example","ntfy_topic":"mitt-amne","ntfy_token":"",
                 "push_subject":"mailto:me@example.com","unrelated":"x","log_level":5}
                """);

            var values = HomeAssistantOptions.Read(file);

            Assert.Equal("sk-abc", values["LifeOS:Ai:OpenAi:ApiKey"]);
            Assert.Equal("https://ntfy.example", values["LifeOS:Notifications:Ntfy:Url"]);
            Assert.Equal("mitt-amne", values["LifeOS:Notifications:Ntfy:Topic"]);
            Assert.Equal("mailto:me@example.com", values["LifeOS:Notifications:WebPush:Subject"]);
            Assert.False(values.ContainsKey("LifeOS:Ai:OpenAi:Model"));       // blank means "use the default"
            Assert.False(values.ContainsKey("LifeOS:Notifications:Ntfy:Token"));
            Assert.Equal(4, values.Count);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Missing_or_broken_ha_options_never_stop_startup()
    {
        Assert.Empty(HomeAssistantOptions.Read(Path.Combine(Path.GetTempPath(), "finns-inte-" + Guid.NewGuid().ToString("N") + ".json")));

        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "{ det här är inte json");
            Assert.Empty(HomeAssistantOptions.Read(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---------- Home Assistant reminders ----------

    private static HomeAssistantReminderClient Reminders(StubHandler handler, string? entity = "todo.mina", string? token = "ha-secret-token") =>
        new(new HttpClient(handler),
            Options.Create(new HomeAssistantApiOptions { Url = "http://ha.example/api", Token = token, ReminderEntity = entity }),
            NullLogger<HomeAssistantReminderClient>.Instance);

    private const string ItemsAnswer = """
        {"changed_states":[],"service_response":{"todo.mina":{"items":[
          {"summary":"Pakethållare","uid":"Reminder/A","status":"needs_action","due":"2026-10-21T11:00:00+00:00"},
          {"summary":"Ziggy ketonmätning","uid":"Reminder/B","status":"needs_action","due":"2026-11-01"},
          {"summary":"Klar sak","uid":"Reminder/C","status":"completed"},
          {"summary":"Utan datum","uid":"Reminder/D","status":"needs_action"},
          {"uid":"Reminder/E","status":"needs_action"}]}}}
        """;

    [Fact]
    public async Task Reminder_items_are_read_with_dates_times_and_completion()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ItemsAnswer, Encoding.UTF8, "application/json") });

        var items = await Reminders(handler).GetItemsAsync();

        Assert.Equal(["Reminder/A", "Reminder/B", "Reminder/C", "Reminder/D"], items.Select(i => i.Uid));   // the item without a summary is skipped
        Assert.Equal(DateTimeOffset.Parse("2026-10-21T11:00:00Z"), items[0].DueAt);
        Assert.Null(items[0].Due);
        Assert.Equal(new DateOnly(2026, 11, 1), items[1].Due);
        Assert.True(items[2].Done);
        Assert.False(items[3].Done);
        Assert.Null(items[3].Due);

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal("http://ha.example/api/services/todo/get_items?return_response", request.RequestUri!.ToString());
        Assert.Equal("Bearer ha-secret-token", request.Headers.Authorization!.ToString());
        Assert.DoesNotContain("ha-secret-token", body);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("todo.mina", doc.RootElement.GetProperty("entity_id").GetString());
    }

    [Fact]
    public async Task Adding_and_updating_use_the_todo_services_with_only_the_given_fields()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        var client = Reminders(handler);

        await client.AddAsync("Köp mjölk", new DateOnly(2026, 10, 14));
        await client.UpdateAsync("Reminder/A", null, null, true);
        await client.UpdateAsync("Reminder/A", "Nytt namn", new DateOnly(2026, 12, 24), null);

        Assert.Equal(
            ["http://ha.example/api/services/todo/add_item", "http://ha.example/api/services/todo/update_item", "http://ha.example/api/services/todo/update_item"],
            handler.Calls.Select(c => c.Request.RequestUri!.ToString()));
        using var add = JsonDocument.Parse(handler.Calls[0].Body!);
        Assert.Equal(("Köp mjölk", "2026-10-14"), (add.RootElement.GetProperty("item").GetString(), add.RootElement.GetProperty("due_date").GetString()));
        using var tick = JsonDocument.Parse(handler.Calls[1].Body!);
        Assert.Equal(("Reminder/A", "completed"), (tick.RootElement.GetProperty("item").GetString(), tick.RootElement.GetProperty("status").GetString()));
        Assert.False(tick.RootElement.TryGetProperty("rename", out _));
        Assert.False(tick.RootElement.TryGetProperty("due_date", out _));
        using var edit = JsonDocument.Parse(handler.Calls[2].Body!);
        Assert.Equal(("Nytt namn", "2026-12-24"), (edit.RootElement.GetProperty("rename").GetString(), edit.RootElement.GetProperty("due_date").GetString()));
        Assert.False(edit.RootElement.TryGetProperty("status", out _));
    }

    [Theory]
    [InlineData(null, "tok")]
    [InlineData("", "tok")]
    [InlineData("todo.mina", null)]
    public async Task Without_an_entity_and_a_token_the_connection_is_off_and_nothing_is_sent(string? entity, string? token)
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var client = Reminders(handler, entity, token);

        if (token is null)
        {
            // The Supervisor normally supplies the token through the environment; absent here, so the connection stays off.
            Environment.SetEnvironmentVariable("SUPERVISOR_TOKEN", null);
        }

        Assert.False(client.IsConfigured);
        Assert.Null(client.ListName);
        await Assert.ThrowsAsync<LifeOS.Application.Abstractions.ReminderException>(() => client.GetItemsAsync());
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Home_assistant_errors_become_a_short_reminder_exception_without_the_answer_text(HttpStatusCode status)
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(status) { Content = new StringContent("SECRET LIST CONTENT") });

        var ex = await Assert.ThrowsAsync<LifeOS.Application.Abstractions.ReminderException>(() => Reminders(handler).GetItemsAsync());

        Assert.Contains(((int)status).ToString(), ex.Message);
        Assert.DoesNotContain("SECRET", ex.Message);
    }

    [Fact]
    public async Task Network_failures_and_unexpected_answers_are_reminder_exceptions()
    {
        var down = new StubHandler((_, _) => throw new HttpRequestException("nere"));
        await Assert.ThrowsAsync<LifeOS.Application.Abstractions.ReminderException>(() => Reminders(down).GetItemsAsync());

        var odd = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"service_response\":{}}", Encoding.UTF8, "application/json") });
        await Assert.ThrowsAsync<LifeOS.Application.Abstractions.ReminderException>(() => Reminders(odd).GetItemsAsync());
    }

    [Fact]
    public void The_reminders_entity_is_mapped_from_the_home_assistant_options()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "{\"reminders_entity\":\"todo.mina\"}");
            Assert.Equal("todo.mina", HomeAssistantOptions.Read(file)["LifeOS:HomeAssistant:ReminderEntity"]);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---------- notifications through the Home Assistant app ----------

    private static HomeAssistantNotifySender Notifier(StubHandler handler, string? service = "notify.mobile_app_telefon", string? token = "ha-secret-token") =>
        new(new HttpClient(handler),
            Options.Create(new HomeAssistantApiOptions { Url = "http://ha.example/api", Token = token, NotifyService = service }),
            NullLogger<HomeAssistantNotifySender>.Instance);

    [Fact]
    public async Task A_notification_is_one_call_to_the_notify_service_with_only_title_and_text()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        var sender = Notifier(handler);

        var outcome = await sender.SendAsync(new NotificationPayload("Dags", "3 uppgifter förfaller idag.", "today"));

        Assert.True(outcome.Delivered);
        Assert.True(sender.IsAvailable);
        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal("http://ha.example/api/services/notify/mobile_app_telefon", request.RequestUri!.ToString());
        Assert.Equal("Bearer ha-secret-token", request.Headers.Authorization!.ToString());
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal(("Dags", "3 uppgifter förfaller idag."), (doc.RootElement.GetProperty("title").GetString(), doc.RootElement.GetProperty("message").GetString()));
        Assert.Equal(2, doc.RootElement.EnumerateObject().Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("light.turn_on")]
    [InlineData("notify.mobile app")]
    [InlineData("notify.../../x")]
    public async Task Only_a_plain_notify_service_name_is_accepted(string? service)
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var sender = Notifier(handler, service);

        Assert.False(sender.IsAvailable);
        Assert.True((await sender.SendAsync(new NotificationPayload("a", "b", ""))).IsNoRecipient);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task The_service_name_works_with_or_without_the_notify_prefix()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        await Notifier(handler, "mobile_app_telefon").SendAsync(new NotificationPayload("a", "b", ""));

        Assert.EndsWith("/services/notify/mobile_app_telefon", handler.Calls[0].Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Home_assistant_errors_and_network_failures_are_failed_outcomes_not_exceptions()
    {
        var notFound = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("SECRET") });
        var failed = await Notifier(notFound).SendAsync(new NotificationPayload("a", "b", ""));
        Assert.False(failed.Delivered);
        Assert.Contains("404", failed.Error);
        Assert.DoesNotContain("SECRET", failed.Error);

        var down = new StubHandler((_, _) => throw new HttpRequestException("nere"));
        var unreachable = await Notifier(down).SendAsync(new NotificationPayload("a", "b", ""));
        Assert.False(unreachable.Delivered);
        Assert.False(unreachable.IsNoRecipient);                                   // a real failure, so it is retried
    }

    [Fact]
    public void The_notify_service_is_mapped_from_the_home_assistant_options()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "{\"notify_service\":\"notify.mobile_app_telefon\"}");
            Assert.Equal("notify.mobile_app_telefon", HomeAssistantOptions.Read(file)["LifeOS:HomeAssistant:NotifyService"]);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
