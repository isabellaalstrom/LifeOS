using LifeOS.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;

namespace LifeOS.Tests.Web;

/// <summary>Runs the real app against a throwaway data directory.</summary>
public sealed class WebAppFactory : WebApplicationFactory<Program>
{
    public const string OwnerEmail = "owner@lifeos.test";
    public const string OwnerPassword = "test-password-1234";

    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "lifeos-web-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("LifeOS:DataPath", _dataPath);
    }

    public HttpClient CreateBrowserClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task EnsureOwnerAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (!users.Users.Any())
        {
            var result = await users.CreateAsync(new AppUser { UserName = OwnerEmail, Email = OwnerEmail }, OwnerPassword);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_dataPath))
            {
                Directory.Delete(_dataPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }
}
