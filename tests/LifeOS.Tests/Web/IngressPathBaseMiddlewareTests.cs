using System.Net;
using LifeOS.Web.Ingress;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LifeOS.Tests.Web;

public class IngressPathBaseMiddlewareTests
{
    private const string ValidPath = "/api/hassio_ingress/Abc-123_xyz";

    private static async Task<PathString> RunAsync(IngressOptions options, string? remoteIp, params string[] headerValues)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp is null ? null : IPAddress.Parse(remoteIp);
        if (headerValues.Length > 0)
        {
            context.Request.Headers[IngressPathBaseMiddleware.HeaderName] = headerValues;
        }

        var middleware = new IngressPathBaseMiddleware(_ => Task.CompletedTask, Options.Create(options), NullLogger<IngressPathBaseMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        return context.Request.PathBase;
    }

    [Fact]
    public async Task Trusted_gateway_with_valid_header_sets_path_base() =>
        Assert.Equal(ValidPath, (await RunAsync(new IngressOptions { Enabled = true }, "172.30.32.2", ValidPath)).Value);

    [Fact]
    public async Task Ipv4_mapped_ipv6_peer_is_matched_against_trusted_ipv4() =>
        Assert.Equal(ValidPath, (await RunAsync(new IngressOptions { Enabled = true }, "::ffff:172.30.32.2", ValidPath)).Value);

    [Fact]
    public async Task Untrusted_peer_cannot_set_path_base() =>
        Assert.False((await RunAsync(new IngressOptions { Enabled = true }, "192.168.1.50", ValidPath)).HasValue);

    [Fact]
    public async Task Unknown_peer_cannot_set_path_base() =>
        Assert.False((await RunAsync(new IngressOptions { Enabled = true }, null, ValidPath)).HasValue);

    [Fact]
    public async Task Header_is_ignored_when_ingress_support_is_disabled() =>
        Assert.False((await RunAsync(new IngressOptions { Enabled = false }, "172.30.32.2", ValidPath)).HasValue);

    [Theory]
    [InlineData("/")]
    [InlineData("//evil.example")]
    [InlineData("/api/hassio_ingress/")]
    [InlineData("/api/hassio_ingress/../admin")]
    [InlineData("/api/hassio_ingress/token/extra")]
    [InlineData("https://evil.example/api/hassio_ingress/token")]
    public async Task Malformed_header_values_are_rejected(string value) =>
        Assert.False((await RunAsync(new IngressOptions { Enabled = true }, "172.30.32.2", value)).HasValue);

    [Fact]
    public async Task Multiple_header_values_are_rejected() =>
        Assert.False((await RunAsync(new IngressOptions { Enabled = true }, "172.30.32.2", ValidPath, ValidPath)).HasValue);

    [Fact]
    public async Task Custom_trusted_addresses_are_honored() =>
        Assert.Equal(ValidPath,
            (await RunAsync(new IngressOptions { Enabled = true, TrustedProxyAddresses = ["127.0.0.1"] }, "127.0.0.1", ValidPath)).Value);
}
