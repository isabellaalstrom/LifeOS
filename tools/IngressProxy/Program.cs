// Local stand-in for Home Assistant ingress, used for the Phase 1 spike.
// Like the Supervisor it: serves the app under /api/hassio_ingress/<token>/, strips that prefix before
// forwarding, adds X-Ingress-Path, and proxies WebSockets (Blazor Server / SignalR).
// It is NOT HA: it adds no HA authentication and does not rewrite response bodies.
//
// Usage: dotnet run --project tools/IngressProxy -- [listenUrl] [appUrl] [token]
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

var listenUrl = args.ElementAtOrDefault(0) ?? "http://127.0.0.1:5180";
var appUrl = args.ElementAtOrDefault(1) ?? "http://127.0.0.1:5169";
var token = args.ElementAtOrDefault(2) ?? "TEST_INGRESS_TOKEN";
var prefix = $"/api/hassio_ingress/{token}";

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ApplicationName = "IngressProxy" });
builder.WebHost.UseUrls(listenUrl);

builder.Services.AddReverseProxy()
    .LoadFromMemory(
        [
            new RouteConfig
            {
                RouteId = "ingress",
                ClusterId = "app",
                Match = new RouteMatch { Path = prefix + "/{**rest}" },
                Transforms =
                [
                    new Dictionary<string, string> { ["PathRemovePrefix"] = prefix },
                    new Dictionary<string, string> { ["RequestHeader"] = "X-Ingress-Path", ["Set"] = prefix },
                ],
            },
        ],
        [
            new ClusterConfig
            {
                ClusterId = "app",
                Destinations = new Dictionary<string, DestinationConfig> { ["app"] = new() { Address = appUrl } },
            },
        ]);

var app = builder.Build();
app.MapGet("/", () => Results.Text($"Ingress simulator. Open {prefix}/"));
app.MapReverseProxy();
Console.WriteLine($"Ingress simulator: {listenUrl}{prefix}/ -> {appUrl}/");
app.Run();
