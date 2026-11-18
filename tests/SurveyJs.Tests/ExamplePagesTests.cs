using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static SurveyJsAspNetCore.Tests.Http;

namespace SurveyJsAspNetCore.Tests;

/// <summary>The pages: every example renders with its module and config, the side-panel layout, /shared and proxies.</summary>
public sealed partial class ExamplePagesTests : IAsyncLifetime
{
    private static readonly JsonArray Steps = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "surveyjs-integration.json")))!["steps"]!.AsArray();

    private static readonly string[] StartOverPages =
        ["save-response", "load-definition-variables-data", "resume-progress", "store-files", "choices-from-web", "async-functions",
         "relational-storage", "validate-response", "extract-from-paper"];

    private readonly TestApp app = new();

    public static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    public static TheoryData<string> Slugs() => new(Steps.Select(step => (string)step!["slug"]!));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => app.DisposeAsync();

    private async Task<string> PageAsync(string path)
    {
        var answer = await app.As().GetAsync(path, Token);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        return await answer.Content.ReadAsStringAsync(Token);
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public async Task EveryExampleRendersWithItsModuleAndConfig(string slug)
    {
        var step = Steps.Single(s => (string?)s!["slug"] == slug)!;
        var html = await PageAsync($"/examples/{slug}");
        Assert.Contains($"<script type=\"module\" src=\"/{step["files"]!["client"]}\"></script>", html);
        Assert.Contains("<script type=\"importmap\">", html);
        var config = JsonNode.Parse(Config().Match(html).Groups[1].Value)!;
        Assert.Equal(slug, (string?)config["slug"]);
        Assert.Equal("3.2.7", (string?)config["version"]);
        foreach (var id in new[] { "side-panel", "panel-tabs", "panel-description", "panel-requests", "panel-stored", "main-tabs", "panel-toggle", "panel-demo", "panel-code", "component", "request-row", "stored-section", "note" })
        {
            Assert.Contains($"id=\"{id}\"", html);
        }
        Assert.Equal(StartOverPages.Contains(slug), html.Contains("id=\"start-over\""));
        Assert.Contains($"{step["title"]} · SurveyJS + ASP.NET Core", html);
        Assert.Contains($"https://surveyjs.io/documentation/backend-integration-aspnet#{step["anchor"]}", html);
        Assert.Contains("data-code-section", html);
        Assert.Contains("Running test", html);
    }

    [Fact]
    public async Task TheCatalogAndTheOtherPagesRender()
    {
        var catalog = await PageAsync("/");
        Assert.Contains("ASP.NET Core Backend Integration · SurveyJS", catalog);
        Assert.Contains("Build an ASP.NET Core Backend for&nbsp;Your SurveyJS Forms", catalog);
        Assert.Equal(18, Regex.Matches(catalog, "Run Example").Count);
        var claim = await PageAsync("/claim");
        Assert.Contains("window.FORM_DEFINITION = {", claim);
        Assert.Contains("window.FORM_VARIABLES = {\"user\":{\"name\":\"Alice Martin\",\"plan\":\"premium\"}}", claim);
        Assert.DoesNotContain("window.FORM_DEFINITION = {", await PageAsync("/examples/load-definition-variables-data"));
        Assert.Contains("Generating the PDF...", await PageAsync("/examples/server-pdf/loading"));
        Assert.Equal(HttpStatusCode.NotFound, (await app.As().GetAsync("/examples/no-such-example", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.As().GetAsync("/examples/edit-together?form=bad%20id", Token)).StatusCode);
    }

    [Fact]
    public async Task InlineJsonCantEndTheScript()
    {
        await app.As().PutJsonAsync("/api/forms/claim-request", """{"title":"</script><script>alert(1)</script>"}""");
        var html = await PageAsync("/examples/extract-from-paper");
        Assert.DoesNotContain("</script><script>alert(1)", html);
        app.Sql("UPDATE forms SET json = '{\"title\":\"</script><b>x</b>\"}' WHERE key = 'work-order'");
        Assert.DoesNotContain("</script><b>x</b>", await PageAsync("/examples/extract-from-paper"));
    }

    [Theory]
    [InlineData("/shared/ui/page-tabs.js", "text/javascript")]
    [InlineData("/shared/client/host.js", "text/javascript")]
    [InlineData("/shared/samples/support-help.svg", "image/svg+xml")]
    [InlineData("/shared/definitions/save-response.json", "application/json")]
    [InlineData("/shared/ui/fonts/lato/LatoLatin-Bold.woff2", "font/woff2")]
    [InlineData("/_content/SurveyJS.Core/fesm/survey-core.mjs", "text/javascript")]
    public async Task SharedServesItsFourFoldersWithTheirTypes(string path, string type)
    {
        var answer = await app.As().GetAsync(path, Token);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(type, answer.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/shared/seed/forms.json")]
    [InlineData("/shared/contract-tests/run.mjs")]
    [InlineData("/shared/SHA256SUMS")]
    [InlineData("/shared/../appsettings.json")]
    [InlineData("/shared/ui/../../appsettings.json")]
    [InlineData("/shared/client/%2e%2e/seed/forms.json")]
    public async Task NothingElseUnderSharedIsReachable(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, (await app.As().SendAsync(request, Token)).StatusCode);
    }

    [Fact]
    public async Task BehindAProxyThePageUsesHttpsAndWss()
    {
        await using var proxied = new TestApp(new() { ["FORWARDEDHEADERS_ENABLED"] = "true" });
        var request = new HttpRequestMessage(HttpMethod.Get, "/examples/fill-together");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Host = "demo.example.com";
        var html = await (await proxied.As().SendAsync(request, Token)).Content.ReadAsStringAsync(Token);
        Assert.Contains("<link rel=\"canonical\" href=\"https://demo.example.com/examples/fill-together\">", html);
        var config = JsonNode.Parse(Config().Match(html).Groups[1].Value)!;
        Assert.Equal("wss://demo.example.com", (string?)config["relay"]!["fill"]!["url"]);
        Assert.Equal("wss://demo.example.com", (string?)config["relay"]!["edit"]!["url"]);
    }

    [GeneratedRegex(@"window\.SURVEYJS_PAGE = (\{.*?\});</script>")]
    private static partial Regex Config();
}
