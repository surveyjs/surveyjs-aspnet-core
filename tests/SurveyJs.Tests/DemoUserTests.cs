using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static SurveyJsAspNetCore.Tests.Http;

namespace SurveyJsAspNetCore.Tests;

/// <summary>The demo users: the per-page lists, the cookie a page sets, Bob's 403s and the signed-out 401s.</summary>
public sealed partial class DemoUserTests : IAsyncLifetime
{
    private readonly TestApp app = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => app.DisposeAsync();

    private async Task<(JsonNode Config, string? Cookie, string Html)> PageAsync(string path, string user)
    {
        var answer = await app.As(user).GetAsync(path, Token);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        var html = await answer.Content.ReadAsStringAsync(Token);
        var config = JsonNode.Parse(PageConfig().Match(html).Groups[1].Value)!;
        var cookie = answer.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.FirstOrDefault(c => c.StartsWith("demo_user=")) : null;
        return (config, cookie, html);
    }

    [Theory]
    [InlineData("/examples/creator-load-save", "bob", "bob", null)]
    [InlineData("/examples/resume-progress", "bob", "bob", null)]
    [InlineData("/claim", "bob", "bob", null)]
    [InlineData("/examples/save-response", "bob", "alice", "demo_user=alice")]
    [InlineData("/examples/creator-load-save", "none", "alice", "demo_user=alice")]
    [InlineData("/examples/dashboard", "mallory", "alice", "demo_user=alice")]
    public async Task EachPageSignsInAsAUserItOffers(string path, string cookie, string user, string? reset)
    {
        var (config, setCookie, _) = await PageAsync(path, cookie);
        Assert.Equal(user, (string?)config["demoUser"]);
        Assert.Equal(user, (string?)config["user"]!["key"]);
        if (reset is null) Assert.Null(setCookie);
        else
        {
            Assert.StartsWith(reset, setCookie);
            Assert.Contains("samesite=lax", setCookie!.ToLowerInvariant());
            Assert.DoesNotContain("httponly", setCookie.ToLowerInvariant());
        }
    }

    [Fact]
    public async Task TheSwitchAppearsOnlyWhereTheUserChangesTheResult()
    {
        Assert.Contains("id=\"demo-user\"", (await PageAsync("/examples/creator-load-save", "alice")).Html);
        Assert.Contains("Customer", (await PageAsync("/examples/load-definition-variables-data", "alice")).Html);
        Assert.DoesNotContain("id=\"demo-user\"", (await PageAsync("/examples/save-response", "alice")).Html);
        Assert.DoesNotContain("id=\"demo-user\"", (await PageAsync("/examples/variable-presets", "alice")).Html);
    }

    [Fact]
    public async Task BobCannotSaveFormsOrPresets()
    {
        const string message = "You don't have editor rights: only editors can change forms";
        Assert.Equal(message, await (await app.As("bob").PutJsonAsync("/api/forms/support", "{}")).ErrorAsync(HttpStatusCode.Forbidden));
        Assert.Equal(message, await (await app.As("bob").PutJsonAsync("/api/variable-presets/support", "[]")).ErrorAsync(HttpStatusCode.Forbidden));
        Assert.Equal(message, await (await app.As("none").PutJsonAsync("/api/forms/support", "{}")).ErrorAsync(HttpStatusCode.Forbidden));
        Assert.Equal(HttpStatusCode.NoContent, (await app.As("alice").PutJsonAsync("/api/forms/support-copy", "{}")).StatusCode);
    }

    [Fact]
    public async Task ProgressNeedsASignedInUserAndBelongsToThem()
    {
        Assert.Equal("Sign in to load progress", await (await app.As("none").GetAsync("/api/progress/onboarding", Token)).ErrorAsync(HttpStatusCode.Unauthorized));
        Assert.Equal("Sign in to save progress", await (await app.As("none").PutJsonAsync("/api/progress/onboarding", "{}")).ErrorAsync(HttpStatusCode.Unauthorized));
        Assert.Equal("Sign in to clear progress", await (await app.As("none").DeleteAsync("/api/progress/onboarding", Token)).ErrorAsync(HttpStatusCode.Unauthorized));

        await app.As("alice").PutJsonAsync("/api/progress/onboarding", """{"data":{"who":"alice"}}""");
        Assert.Equal("null", await app.As("bob").TextAsync("/api/progress/onboarding"));
        Assert.Equal(HttpStatusCode.NoContent, (await app.As("bob").DeleteAsync("/api/progress/onboarding", Token)).StatusCode);
        Assert.Contains("alice", await app.As("alice").TextAsync("/api/progress/onboarding"));
    }

    [Fact]
    public async Task ClaimRequestsAreScopedToTheSignedInCustomer()
    {
        var bobsRecord = app.Sql("SELECT id FROM responses WHERE form_id = 'claim' AND json_extract(data, '$.customer_email') = 'bob@example.com'").Single();
        var asBob = (await (await app.As("bob").GetAsync($"/api/forms/claim-request?record={bobsRecord}", Token)).JsonAsync())!;
        Assert.Equal("bob@example.com", (string?)asBob["data"]!["customer_email"]);
        Assert.Equal("Bob Fischer", (string?)asBob["variables"]!["user"]!["name"]);
        var asAlice = (await (await app.As("alice").GetAsync($"/api/forms/claim-request?record={bobsRecord}", Token)).JsonAsync())!;
        Assert.Equal("{}", asAlice["data"]!.ToJsonString());
        Assert.Equal("premium", (string?)asAlice["variables"]!["user"]!["plan"]);
        // The page asks with an empty ?record= for a new response
        var empty = await app.As("alice").GetAsync("/api/forms/claim-request?record=", Token);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Equal("{}", (await empty.JsonAsync())!["data"]!.ToJsonString());
    }

    [GeneratedRegex(@"window\.SURVEYJS_PAGE = (\{.*?\});</script>")]
    private static partial Regex PageConfig();
}
