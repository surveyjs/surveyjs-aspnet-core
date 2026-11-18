using System.Net;
using System.Text.Json.Nodes;
using static SurveyJsAspNetCore.Tests.Http;

namespace SurveyJsAspNetCore.Tests;

/// <summary>JSON is stored exactly as it arrives, through every storing endpoint, and read back from SQLite as raw text.</summary>
public sealed class StoredJsonTests : IAsyncLifetime
{
    /// <summary>The contract test's round-trip document, plus 1.0 (the number text JsonNode keeps).</summary>
    private const string Document = """{"a":"  padded  ","b":"","c":null,"d":{},"e":[{}],"f":[],"g":1.5,"h":"naïve — 東京","i":{"j":{"k":[1,"x",true]}},"z":1.0}""";

    private readonly TestApp app = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => app.DisposeAsync();

    [Fact]
    public async Task SaveResponseStoresTheDocumentAsItArrived()
    {
        var answer = await app.As().PostJsonAsync("/api/responses", $$$"""{"formId":"contact","data":{{{Document}}}}""");
        Assert.Equal(HttpStatusCode.Created, answer.StatusCode);
        var id = (long)(await answer.JsonAsync())!["id"]!;
        Assert.Equal(Document, app.Sql($"SELECT data FROM responses WHERE id = {id}").Single());
    }

    [Fact]
    public async Task ProgressPresetsAndFormsAreStoredAsTheyArrived()
    {
        var client = app.As("alice");
        var progress = $$$"""{"data":{{{Document}}},"uiState":{"currentPageNo":1}}""";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutJsonAsync("/api/progress/onboarding", progress)).StatusCode);
        Assert.Equal(progress, app.Sql("SELECT json FROM progress WHERE key = '1:onboarding'").Single());
        Assert.Equal(progress, await client.TextAsync("/api/progress/onboarding"));

        var presets = $$"""[{"name":"Edge","variables":{{Document}}}]""";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutJsonAsync("/api/variable-presets/support", presets)).StatusCode);
        Assert.Equal(presets, app.Sql("SELECT json FROM variable_presets WHERE key = 'support'").Single());

        var definition = $$$"""{"title":"Round trip","x":{{{Document}}}}""";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutJsonAsync("/api/forms/round-trip", definition)).StatusCode);
        Assert.Equal(definition, app.Sql("SELECT json FROM forms WHERE key = 'round-trip'").Single());
        Assert.Equal(JsonNode.Parse(definition)!.ToJsonString(), (await (await client.GetAsync("/api/forms/round-trip", Token)).JsonAsync())!["definition"]!.ToJsonString());
    }

    [Fact]
    public async Task RelationalStorageKeepsTheOriginalAndFillsTheColumns()
    {
        var data = $$$"""{"customer_email":"x@example.com","amount":12.50,"extra":{{{Document}}}}""";
        var answer = await app.As().PostJsonAsync("/api/claims", $$$"""{"definitionVersion":"v2","data":{{{data}}}}""");
        Assert.Equal(HttpStatusCode.Created, answer.StatusCode);
        var id = (long)(await answer.JsonAsync())!["id"]!;
        Assert.Equal(data, app.Sql($"SELECT data FROM responses WHERE id = {id}").Single());
        Assert.Equal("v2", app.Sql($"SELECT definition_version FROM responses WHERE id = {id}").Single());
        Assert.Equal("x@example.com", app.Sql($"SELECT customer_email FROM claims WHERE response_id = {id}").Single());
    }

    [Fact]
    public async Task DashboardReturnsTheDocumentsAsStored()
    {
        var client = app.As();
        await client.PostJsonAsync("/api/responses", $$$"""{"formId":"round-trip","data":{{{Document}}}}""");
        Assert.Equal($"[{Document}]", await client.TextAsync("/api/responses?formId=round-trip"));
    }

    [Fact]
    public async Task TwoSimultaneousPutsToOneProgressKeyBothSucceed()
    {
        var client = app.As("alice");
        var answers = await Task.WhenAll(
            client.PutJsonAsync("/api/progress/race", """{"data":{"n":1}}"""),
            client.PutJsonAsync("/api/progress/race", """{"data":{"n":2}}"""));
        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.NoContent, answer.StatusCode));
        Assert.Single(app.Sql("SELECT json FROM progress WHERE key = '1:race'"));
    }

    [Fact]
    public async Task ABodyThatIsNotJsonAnswers400()
    {
        Assert.Equal("The request body is not valid JSON", await (await app.As().PostJsonAsync("/api/responses", "{nope")).ErrorAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Expected { formId, data }", await (await app.As().PostJsonAsync("/api/responses", "[1]")).ErrorAsync(HttpStatusCode.BadRequest));
    }
}
