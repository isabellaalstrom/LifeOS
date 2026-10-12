using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace LifeOS.Web.Ingress;

/// <summary>
/// Maps the Home Assistant ingress prefix (X-Ingress-Path) to the request PathBase so that links,
/// static assets, cookies and the Blazor circuit endpoint all resolve under the ingress URL.
/// The header is only honored from trusted peers and only in the expected format; it is never an identity.
/// </summary>
public sealed partial class IngressPathBaseMiddleware(
    RequestDelegate next,
    IOptions<IngressOptions> options,
    ILogger<IngressPathBaseMiddleware> logger)
{
    public const string HeaderName = "X-Ingress-Path";

    [GeneratedRegex(@"^/api/hassio_ingress/[A-Za-z0-9_\-]{1,128}$")]
    private static partial Regex ValidPath();

    public Task InvokeAsync(HttpContext context)
    {
        var settings = options.Value;
        if (settings.Enabled && context.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is { IsIPv4MappedToIPv6: true })
            {
                remote = remote.MapToIPv4();
            }

            var trusted = remote is not null
                && settings.TrustedProxyAddresses.Any(a => IPAddress.TryParse(a, out var ip) && ip.Equals(remote));

            if (trusted && values.Count == 1 && ValidPath().IsMatch(values[0]!))
            {
                context.Request.PathBase = new PathString(values[0]);
            }
            else
            {
                logger.LogWarning("Ignored {Header} from {Remote}", HeaderName, remote);
            }
        }

        return next(context);
    }
}
