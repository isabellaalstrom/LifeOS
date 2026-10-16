using System.Net;
using System.Text;
using System.Text.Json;
using LifeOS.Application.Abstractions;
using LifeOS.Infrastructure.Ai;
using LifeOS.Infrastructure.Notifications;
using LifeOS.Infrastructure.Persistence;
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
}
