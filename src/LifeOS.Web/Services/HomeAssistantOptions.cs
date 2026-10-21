using System.Text.Json;

namespace LifeOS.Web.Services;

/// <summary>
/// Maps the Home Assistant app options (/data/options.json) to LifeOS configuration, so secrets are entered in the HA UI
/// and never stored in the repository. Empty values are ignored. Does nothing outside Home Assistant.
/// </summary>
public static class HomeAssistantOptions
{
    private static readonly Dictionary<string, string> Mapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai_api_key"] = "LifeOS:Ai:OpenAi:ApiKey",
        ["openai_model"] = "LifeOS:Ai:OpenAi:Model",
        ["ntfy_url"] = "LifeOS:Notifications:Ntfy:Url",
        ["ntfy_topic"] = "LifeOS:Notifications:Ntfy:Topic",
        ["ntfy_token"] = "LifeOS:Notifications:Ntfy:Token",
        ["push_subject"] = "LifeOS:Notifications:WebPush:Subject",
        ["reminders_entity"] = "LifeOS:HomeAssistant:ReminderEntity",
    };

    public static IConfigurationBuilder AddHomeAssistantOptions(this IConfigurationBuilder builder, string? path = null)
    {
        path ??= Environment.GetEnvironmentVariable("LIFEOS_HA_OPTIONS") ?? "/data/options.json";
        return builder.AddInMemoryCollection(Read(path));
    }

    public static IReadOnlyDictionary<string, string?> Read(string path)
    {
        var values = new Dictionary<string, string?>();
        if (!File.Exists(path))
        {
            return values;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (Mapping.TryGetValue(property.Name, out var key)
                    && property.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    values[key] = property.Value.GetString();
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // An unreadable options file must never stop the app from starting.
        }

        return values;
    }
}
