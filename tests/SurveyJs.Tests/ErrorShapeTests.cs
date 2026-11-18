using System.Net;
using System.Text;
using SurveyJsAspNetCore.Demo;
using static SurveyJsAspNetCore.Tests.Http;

namespace SurveyJsAspNetCore.Tests;

/// <summary>Every error under /api is JSON { error } with its status, the framework's own answers included.</summary>
public sealed class ErrorShapeTests : IAsyncLifetime
{
    private readonly TestApp app = new(new() { ["DEMO_MODE"] = "true", ["AI_API_KEY"] = "test", ["AI_BASE_URL"] = "http://127.0.0.1:9/v1" });

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => app.DisposeAsync();

    [Fact]
    public async Task Status400ForABodyThatIsNotJsonAndForAFormPostToAJsonEndpoint()
    {
        Assert.Equal("The request body is not valid JSON", await (await app.As().PostJsonAsync("/api/responses", "{")).ErrorAsync(HttpStatusCode.BadRequest));
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["formId"] = "contact" });
        Assert.Equal("The request body is not valid JSON", await (await app.As().PostAsync("/api/responses", form, Token)).ErrorAsync(HttpStatusCode.BadRequest));
        Assert.NotNull(await (await app.As().GetAsync("/api/responses?formId=x&limit=many", Token)).ErrorAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Status401403And404()
    {
        Assert.Equal("Sign in to load progress", await (await app.As("none").GetAsync("/api/progress/onboarding", Token)).ErrorAsync(HttpStatusCode.Unauthorized));
        Assert.Equal("You don't have editor rights: only editors can change forms",
            await (await app.As("bob").PutJsonAsync("/api/forms/support", "{}")).ErrorAsync(HttpStatusCode.Forbidden));
        Assert.Equal("Not Found", await (await app.As().GetAsync("/api/no-such-endpoint", Token)).ErrorAsync(HttpStatusCode.NotFound));
        Assert.Equal("Unknown form", await (await app.As().GetAsync("/api/forms/no-such-form", Token)).ErrorAsync(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Status405ForAWrongMethodAnd415ForAJsonUpload()
    {
        Assert.NotNull(await (await app.As().DeleteAsync("/api/forms/support", Token)).ErrorAsync(HttpStatusCode.MethodNotAllowed));
        Assert.NotNull(await (await app.As().PutJsonAsync("/api/responses", "{}")).ErrorAsync(HttpStatusCode.MethodNotAllowed));
        Assert.NotNull(await (await app.As().PostJsonAsync("/api/files", "{}")).ErrorAsync(HttpStatusCode.UnsupportedMediaType));
    }

    [Fact]
    public async Task Status413ForAnUploadOver5MbOnTheDemo()
    {
        var big = new byte[5 * 1024 * 1024 + 1];
        Http.Png.CopyTo(big, 0);
        var answer = await app.As().PostAsync("/api/files", Files(("files[]", "big.png", big)), Token);
        Assert.Equal("Files over 5 MB are not accepted on the demo", await answer.ErrorAsync(HttpStatusCode.RequestEntityTooLarge));
    }

    [Fact]
    public async Task Status429AfterTheDemosAiCallsAnd500ForAnUnexpectedError()
    {
        var client = app.As("alice", Sandboxes.NewId());
        var body = """{"strings":["Hello"],"from":"en","to":"fr"}""";
        for (var i = 0; i < 20; i++)
        {
            // The provider at AI_BASE_URL isn't running: the endpoint answers 502, and the call still counts
            Assert.Equal(HttpStatusCode.BadGateway, (await client.PostJsonAsync("/api/translate", body)).StatusCode);
        }
        Assert.Equal("The demo allows 20 AI calls per visitor per day", await (await client.PostJsonAsync("/api/translate", body)).ErrorAsync(HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task Status500IsJsonWithoutDetails()
    {
        // A claim whose amount isn't a number can't be copied into the decimal column: an exception in the endpoint
        var answer = await app.As().PostJsonAsync("/api/claims", """{"definitionVersion":"v1","data":{"amount":"lots"}}""");
        Assert.Equal("Internal server error", await answer.ErrorAsync(HttpStatusCode.InternalServerError));
        Assert.DoesNotContain("lots", Encoding.UTF8.GetString(await answer.Content.ReadAsByteArrayAsync(Token)));
    }
}
