using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOS.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LifeOS.Infrastructure.Ai;

public sealed class OpenAiOptions
{
    public const string SectionName = "LifeOS:Ai:OpenAi";

    /// <summary>Never stored in the repository. Set via environment variable or the Home Assistant app options.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-4o-mini";

    public string BaseUrl { get; set; } = "https://api.openai.com/";

    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// OpenAI chat completions over HTTPS. The API key only ever lives on the server. Request and response bodies are never logged
/// (they contain personal reflections); only status codes are.
/// </summary>
public sealed class OpenAiClient(HttpClient http, IOptions<OpenAiOptions> options, ILogger<OpenAiClient> logger) : IAiClient
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return AiCompletion.Failed(AiFailure.NotConfigured);
        }

        var messages = new List<object> { new { role = "system", content = request.System } };
        messages.AddRange(request.Messages.Select(m => new { role = m.Role, content = m.Content }));

        var body = new Dictionary<string, object?>
        {
            ["model"] = options.Value.Model,
            ["messages"] = messages,
            ["max_completion_tokens"] = request.MaxOutputTokens,
        };
        if (request.JsonOutput)
        {
            body["response_format"] = new { type = "json_object" };
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);

        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AI provider answered {StatusCode}", (int)response.StatusCode);
                return AiCompletion.Failed(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiFailure.Rejected,
                    HttpStatusCode.TooManyRequests => AiFailure.RateLimited,
                    >= HttpStatusCode.InternalServerError => AiFailure.Unavailable,
                    HttpStatusCode.RequestTimeout => AiFailure.Unavailable,
                    _ => AiFailure.BadResponse,
                });
            }

            var parsed = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken);
            var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            return string.IsNullOrWhiteSpace(text) ? AiCompletion.Failed(AiFailure.BadResponse) : AiCompletion.Success(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("AI provider call failed: {Type}", ex.GetType().Name);
            return AiCompletion.Failed(ex is JsonException ? AiFailure.BadResponse : AiFailure.Unavailable);
        }
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public ChoiceMessage? Message { get; set; }
    }

    private sealed class ChoiceMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
