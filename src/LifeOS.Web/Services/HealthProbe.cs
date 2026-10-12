namespace LifeOS.Web.Services;

/// <summary>
/// `dotnet LifeOS.Web.dll --healthcheck` for container HEALTHCHECKs (the runtime image has no curl).
/// Exits 0 when /health answers 200, otherwise 1.
/// </summary>
public static class HealthProbe
{
    public const string Argument = "--healthcheck";

    public static async Task<int> RunAsync()
    {
        var url = Environment.GetEnvironmentVariable("LIFEOS_HEALTHCHECK_URL") ?? "http://127.0.0.1:8080/health";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
