using System.Net;
using LifeOS.Application;
using LifeOS.Application.Abstractions;
using LifeOS.Infrastructure;
using LifeOS.Infrastructure.Identity;
using LifeOS.Infrastructure.Persistence;
using LifeOS.Web.Components;
using LifeOS.Web.Ingress;
using LifeOS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

if (args.Contains(HealthProbe.Argument))
{
    return await HealthProbe.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOptions<LifeOSOptions>()
    .PostConfigure<IHostEnvironment>((o, env) => o.DataPath = Path.GetFullPath(o.DataPath, env.ContentRootPath));
builder.Services.Configure<IngressOptions>(builder.Configuration.GetSection("LifeOS:Ingress"));

// Keys must survive restarts (auth cookies, antiforgery) so they live next to the database in the data volume.
builder.Services.AddDataProtection().SetApplicationName("LifeOS");
builder.Services.AddOptions<KeyManagementOptions>().Configure<IOptions<LifeOSOptions>, ILoggerFactory>(
    (keys, lifeOs, loggerFactory) =>
        keys.XmlRepository = new FileSystemXmlRepository(
            new DirectoryInfo(Path.Combine(lifeOs.Value.DataPath, "keys")), loggerFactory));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<AppEvents>();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies(cookies => cookies.ApplicationCookie!.Configure(o =>
    {
        o.Cookie.Name = ".LifeOS.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        // Plain HTTP is expected behind HA ingress and on a home LAN; Secure is added when the request is HTTPS.
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
    }));
builder.Services.AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 10;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireDigit = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddMudServices();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

// Order matters: the path-base middleware must see the real peer address, so it runs before forwarded
// headers rewrite RemoteIpAddress. Both are active only when ingress support is explicitly enabled.
app.UseMiddleware<IngressPathBaseMiddleware>();
var ingress = app.Services.GetRequiredService<IOptions<IngressOptions>>().Value;
if (ingress.Enabled)
{
    var forwarded = new ForwardedHeadersOptions
    {
        // XForwardedFor is included so the KnownProxies check applies to the whole header set;
        // without it Host/Proto would be accepted from any peer.
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
        ForwardLimit = 1,
    };
    forwarded.KnownProxies.Clear();
    forwarded.KnownIPNetworks.Clear();
    foreach (var address in ingress.TrustedProxyAddresses)
    {
        forwarded.KnownProxies.Add(IPAddress.Parse(address));
    }

    app.UseForwardedHeaders(forwarded);
}

app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("sv-SE")
    .AddSupportedCultures("sv-SE", "en")
    .AddSupportedUICultures("sv-SE", "en"));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();

// Authenticated diagnostics for the HA Ingress spike: shows how the app sees the request. No cookies or secrets.
app.MapGet("/diagnostics/request", (HttpContext http, IOptions<IngressOptions> options) => Results.Ok(new
{
    ingressEnabled = options.Value.Enabled,
    scheme = http.Request.Scheme,
    host = http.Request.Host.Value,
    pathBase = http.Request.PathBase.Value,
    path = http.Request.Path.Value,
    remoteIp = http.Connection.RemoteIpAddress?.ToString(),
    isWebSocketRequest = http.WebSockets.IsWebSocketRequest,
    receivedHeaders = http.Request.Headers
        // ForwardedHeaders moves processed X-Forwarded-* values to X-Original-*.
        .Where(h => h.Key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
                    || h.Key.StartsWith("X-Original-", StringComparison.OrdinalIgnoreCase)
                    || h.Key.Equals("X-Ingress-Path", StringComparison.OrdinalIgnoreCase)
                    || h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(h => h.Key, h => h.Value.ToString()),
}));

app.MapPost("/account/logout", async (SignInManager<AppUser> signInManager, [FromForm] string? ignored) =>
{
    await signInManager.SignOutAsync();
    return Results.LocalRedirect("~/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
return 0;

public partial class Program;
