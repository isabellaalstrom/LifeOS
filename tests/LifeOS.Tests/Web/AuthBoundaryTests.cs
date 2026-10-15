using System.Net;
using System.Text.RegularExpressions;

namespace LifeOS.Tests.Web;

public partial class AuthBoundaryTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public AuthBoundaryTests(WebAppFactory factory) => _factory = factory;

    [GeneratedRegex("""<input[^>]*type="hidden"[^>]*>""")]
    private static partial Regex HiddenInput();

    [GeneratedRegex(@"(\w+)=""([^""]*)""")]
    private static partial Regex Attribute();

    [Fact]
    public async Task Health_is_anonymous_and_reports_healthy()
    {
        var response = await _factory.CreateBrowserClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/goals")]
    [InlineData("/planning")]
    [InlineData("/projects")]
    [InlineData("/habits")]
    [InlineData("/retros")]
    public async Task Anonymous_requests_to_app_pages_are_redirected_to_login(string path)
    {
        await _factory.EnsureOwnerAsync();
        var response = await _factory.CreateBrowserClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Setup_is_closed_once_the_owner_exists()
    {
        await _factory.EnsureOwnerAsync();
        var response = await _factory.CreateBrowserClient().GetAsync("/setup");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Logout_without_antiforgery_token_is_rejected()
    {
        await _factory.EnsureOwnerAsync();
        var client = _factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Redirect, (await PostLoginAsync(client, WebAppFactory.OwnerPassword)).StatusCode);

        var response = await client.PostAsync("/account/logout", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/planning")).StatusCode); // still signed in
    }

    [Fact]
    public async Task Logout_with_antiforgery_token_signs_the_user_out()
    {
        await _factory.EnsureOwnerAsync();
        var client = _factory.CreateBrowserClient();
        await PostLoginAsync(client, WebAppFactory.OwnerPassword);

        var html = await client.GetStringAsync("/planning");
        var logoutForm = Regex.Match(html, @"<form[^>]*action=""account/logout""[^>]*>.*?</form>", RegexOptions.Singleline).Value;
        var token = Regex.Match(logoutForm, @"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "logout form should contain an antiforgery token");

        var response = await client.PostAsync("/account/logout", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token) }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/planning")).StatusCode); // back to login
    }

    [Fact]
    public async Task Wrong_password_does_not_sign_in_and_correct_password_grants_access()
    {
        await _factory.EnsureOwnerAsync();
        var client = _factory.CreateBrowserClient();

        var bad = await PostLoginAsync(client, WebAppFactory.OwnerPassword + "-wrong");
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode); // form re-rendered with an error
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/planning")).StatusCode);

        var good = await PostLoginAsync(client, WebAppFactory.OwnerPassword);
        Assert.Equal(HttpStatusCode.Redirect, good.StatusCode);

        var page = await client.GetAsync("/planning");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Planering", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Navigation_is_server_rendered_without_needing_a_blazor_circuit()
    {
        // Regression: the side menu once depended on MudBlazor JS and never appeared behind HA ingress.
        await _factory.EnsureOwnerAsync();
        var client = _factory.CreateBrowserClient();
        await PostLoginAsync(client, WebAppFactory.OwnerPassword);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/planning"));
        Assert.Contains("class=\"side-nav\"", html);
        Assert.Contains("class=\"bottom-nav\"", html);
        foreach (var section in new[] { "Översikt", "Mål", "Planering", "Projekt", "Vanor", "Retron" })
        {
            Assert.Contains(section, html);
        }
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string password)
    {
        var html = await client.GetStringAsync("/login");
        var fields = new Dictionary<string, string>();
        foreach (Match input in HiddenInput().Matches(html))
        {
            var attributes = Attribute().Matches(input.Value).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            if (attributes.TryGetValue("name", out var name))
            {
                fields[name] = System.Net.WebUtility.HtmlDecode(attributes.GetValueOrDefault("value", ""));
            }
        }

        fields["Input.Email"] = WebAppFactory.OwnerEmail;
        fields["Input.Password"] = password;
        return await client.PostAsync("/login", new FormUrlEncodedContent(fields));
    }
}
