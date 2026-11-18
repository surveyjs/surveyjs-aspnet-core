using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using static SurveyJsAspNetCore.Tests.Http;

namespace SurveyJsAspNetCore.Tests;

/// <summary>
/// Section IV: every row of the status mapping and every check before the call, with a stub in place of the SurveyJS
/// service. Each test asserts the status, the body, whether anything was stored, and that answers never reach the log.
/// </summary>
public sealed class SurveyJsServiceTests : IAsyncLifetime
{
    private const string Marker = "secret-answer-7731";
    private readonly TestApp app;
    private readonly StubService service;

    public SurveyJsServiceTests() => (app, service) = StubService.App();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => app.DisposeAsync();

    private int Responses(string formId = "claim") => int.Parse(app.Sql($"SELECT COUNT(*) FROM responses WHERE form_id = '{formId}'").Single()!);

    private string? Form(string key) => app.Sql($"SELECT json FROM forms WHERE key = '{key}'").SingleOrDefault();

    private Task<HttpResponseMessage> Validate(string body = $$$"""{"formId":"claim","data":{"customer_email":"{{{Marker}}}"}}""") =>
        app.As().PostJsonAsync("/api/responses", body);

    private void Answers(int status, string body, string type = "application/json") => service.Answer = (_, _) => StubService.Reply(status, body, type);

    // ---- validate-response

    [Fact]
    public async Task ValidateSavesOnlyForAnEmptyObject()
    {
        _ = app.Server;
        var before = Responses();
        Answers(200, "{}");
        var answer = await Validate();
        Assert.Equal(HttpStatusCode.Created, answer.StatusCode);
        Assert.True((long)(await answer.JsonAsync())!["id"]! > 0);
        Assert.Equal(before + 1, Responses());
        Assert.Equal("/response", service.Calls.Single().Path);
        Assert.NotNull(JsonNode.Parse(service.Calls.Single().Body)!["schema"]);
    }

    [Fact]
    public async Task ValidateAnswers400WithTheErrorsWhenTheAnswersDontFit()
    {
        _ = app.Server;
        var before = Responses();
        Answers(422, """{"errors":[{"name":"amount","text":"Response required."}]}""");
        var answer = await Validate();
        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Equal("amount", (string?)(await answer.JsonAsync())!["errors"]![0]!["name"]);
        Assert.Equal(before, Responses());
    }

    [Theory]
    [InlineData(422, """{"errors":[],"warnings":[]}""", HttpStatusCode.BadGateway, "The stored definition has errors")]
    [InlineData(422, """{"warnings":[]}""", HttpStatusCode.BadGateway, "SurveyJS service error")]
    [InlineData(200, """{"error":"schema is required"}""", HttpStatusCode.BadGateway, "SurveyJS service error")]
    [InlineData(200, "not json", HttpStatusCode.BadGateway, "SurveyJS service error")]
    [InlineData(500, """{"error":"boom"}""", HttpStatusCode.BadGateway, "SurveyJS service error")]
    public async Task ValidateFailsClosed(int status, string body, HttpStatusCode expected, string error)
    {
        _ = app.Server;
        var before = Responses();
        Answers(status, body);
        Assert.Equal(error, await (await Validate()).ErrorAsync(expected));
        Assert.Equal(before, Responses());
        Assert.DoesNotContain(app.Logs, line => line.Contains(Marker));
    }

    [Fact]
    public async Task ValidateChecksTheEnvelopeAndTheFormBeforeCallingTheService()
    {
        _ = app.Server;
        Assert.Equal("Expected { formId, data } with an answers object", await (await Validate("""{"formId":"claim","data":[]}""")).ErrorAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Expected { formId, data } with an answers object", await (await Validate("""{"formId":7,"data":{}}""")).ErrorAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Unknown form", await (await Validate("""{"formId":"nope","data":{}}""")).ErrorAsync(HttpStatusCode.NotFound));
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task ATimeoutAnswers504AndARefusedConnection503()
    {
        _ = app.Server;
        service.Answer = (_, _) => throw new TaskCanceledException("The request was canceled due to the configured timeout");
        Assert.Equal("SurveyJS service timed out", await (await Validate()).ErrorAsync(HttpStatusCode.GatewayTimeout));
        service.Answer = (_, _) => throw new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused));
        Assert.Equal("SurveyJS service is not running at http://127.0.0.1:9", await (await Validate()).ErrorAsync(HttpStatusCode.ServiceUnavailable));
        service.Answer = (_, _) => throw new HttpRequestException("The response ended prematurely");
        Assert.Equal("SurveyJS service error", await (await Validate()).ErrorAsync(HttpStatusCode.BadGateway));
    }

    // ---- lint-definition

    [Fact]
    public async Task LintSavesForNoFindingsAndForWarningsOnly()
    {
        _ = app.Server;
        Answers(200, "{}");
        Assert.Equal(HttpStatusCode.NoContent, (await app.As().PutJsonAsync("/api/forms/support-lint", """{"title":"clean"}""")).StatusCode);
        Assert.Contains("clean", Form("support-lint"));

        Answers(200, """{"warnings":[{"rule":"x"}]}""");
        var answer = await app.As().PutJsonAsync("/api/forms/support-lint", """{"title":"warned"}""");
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("x", (string?)(await answer.JsonAsync())!["warnings"]![0]!["rule"]);
        Assert.Contains("warned", Form("support-lint"));
    }

    [Fact]
    public async Task LintRefusesErrorsAndKeepsThePreviousVersion()
    {
        _ = app.Server;
        var before = Form("support-lint");
        Answers(422, """{"errors":[{"rule":"broken"}],"warnings":[]}""");
        var answer = await app.As().PutJsonAsync("/api/forms/support-lint", """{"title":"broken"}""");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, answer.StatusCode);
        Assert.Equal("broken", (string?)(await answer.JsonAsync())!["errors"]![0]!["rule"]);
        Assert.Equal(before, Form("support-lint"));
    }

    [Theory]
    [InlineData(200, """{"error":"schema is required"}""")]
    [InlineData(200, """{"warnings":[],"extra":1}""")]
    [InlineData(422, """{"errors":[]}""")]
    [InlineData(400, """{"error":"INVALID_SCHEMA"}""")]
    public async Task LintFailsClosed(int status, string body)
    {
        _ = app.Server;
        var before = Form("support-lint");
        Answers(status, body);
        Assert.Equal("SurveyJS service error", await (await app.As().PutJsonAsync("/api/forms/support-lint", "{}")).ErrorAsync(HttpStatusCode.BadGateway));
        Assert.Equal(before, Form("support-lint"));
    }

    [Fact]
    public async Task LintChecksTheEditorFirst()
    {
        _ = app.Server;
        Assert.NotNull(await (await app.As("bob").PutJsonAsync("/api/forms/support-lint", "{}")).ErrorAsync(HttpStatusCode.Forbidden));
        Assert.Empty(service.Calls);
    }

    // ---- server-pdf and the blank work order

    private long ClaimId(string? version) =>
        long.Parse(app.Sql($"SELECT id FROM responses WHERE form_id = 'claim' AND definition_version {(version is null ? "IS NULL" : $"= '{version}'")} LIMIT 1").Single()!);

    [Fact]
    public async Task ServerPdfPassesThePdfThrough()
    {
        _ = app.Server;
        service.Answer = (_, _) => StubService.Bytes(200, Pdf, "application/pdf");
        var id = ClaimId("v1");
        var answer = await app.As().GetAsync($"/api/claims/{id}/pdf", Token);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("application/pdf", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"inline; filename=\"claim-{id}.pdf\"", answer.Content.Headers.ContentDisposition?.ToString());
        Assert.Equal("private, no-store", answer.Headers.NonValidated["Cache-Control"].ToString());
        Assert.Equal(Pdf, await answer.Content.ReadAsByteArrayAsync(Token));
        var sent = JsonNode.Parse(service.Calls.Single().Body)!;
        Assert.NotNull(sent["schema"]);
        Assert.NotNull(sent["response"]);
    }

    [Theory]
    [InlineData("application/pdf", "not a pdf")]
    [InlineData("application/json", "%PDF-1.7")]
    public async Task ServerPdfFailsClosed(string type, string body)
    {
        _ = app.Server;
        Answers(200, body, type);
        Assert.Equal("SurveyJS service error", await (await app.As().GetAsync($"/api/claims/{ClaimId("v1")}/pdf", Token)).ErrorAsync(HttpStatusCode.BadGateway));
    }

    [Fact]
    public async Task ServerPdfAnswers404And409WithoutCallingTheService()
    {
        _ = app.Server;
        Assert.Equal("Unknown claim", await (await app.As().GetAsync("/api/claims/999999/pdf", Token)).ErrorAsync(HttpStatusCode.NotFound));
        app.Sql($"UPDATE responses SET definition_version = 'v9' WHERE id = {ClaimId("v1")}");
        Assert.Equal("The schema version for this claim is unavailable",
            await (await app.As().GetAsync($"/api/claims/{ClaimId("v9")}/pdf", Token)).ErrorAsync(HttpStatusCode.Conflict));
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task TheBlankWorkOrderIsAnAttachment()
    {
        _ = app.Server;
        service.Answer = (_, _) => StubService.Bytes(200, Pdf, "application/pdf");
        var answer = await app.As().GetAsync("/api/work-orders/blank.pdf", Token);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("attachment; filename=\"work-order.pdf\"", answer.Content.Headers.ContentDisposition?.ToString());
        Assert.Null(JsonNode.Parse(service.Calls.Single().Body)!["response"]);
        app.Sql("DELETE FROM forms WHERE key = 'work-order'");
        Assert.Equal("Unknown work order form", await (await app.As().GetAsync("/api/work-orders/blank.pdf", Token)).ErrorAsync(HttpStatusCode.NotFound));
    }

    // ---- extract-from-paper

    private Task<HttpResponseMessage> Extract(byte[]? scan = null, string field = "scan") =>
        app.As().PostAsync("/api/work-orders/extract", Files((field, "scan.png", scan ?? Png)), Token);

    [Fact]
    public async Task ExtractReturnsADraftAndSavesNothing()
    {
        _ = app.Server;
        var before = int.Parse(app.Sql("SELECT COUNT(*) FROM responses").Single()!);
        Answers(200, $$"""{"data":{"customer":"{{Marker}}","hours":1.0},"confidence":[{"fieldName":"customer","value":"x","confidence":0.4,"flagged":true}],"uniqueId":"WO-1"}""");
        var answer = await Extract();
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        var body = (await answer.JsonAsync())!;
        Assert.Equal(Marker, (string?)body["answers"]!["customer"]);
        Assert.Equal("1.0", body["answers"]!["hours"]!.ToJsonString());
        Assert.Equal("WO-1", (string?)body["uniqueId"]);
        Assert.True((bool)body["confidence"]![0]!["flagged"]!);
        Assert.Equal(before, int.Parse(app.Sql("SELECT COUNT(*) FROM responses").Single()!));
        var sent = JsonNode.Parse(service.Calls.Single().Body)!;
        Assert.Equal(Convert.ToBase64String(Png), (string?)sent["document"]);
        Assert.NotNull(sent["schema"]);
    }

    [Theory]
    [InlineData(400, """{"error":"INVALID_DOCUMENT"}""", HttpStatusCode.BadRequest, "The file is not a PDF, PNG, JPEG, WebP or GIF document")]
    [InlineData(503, """{"error":"AI_NOT_CONFIGURED"}""", HttpStatusCode.ServiceUnavailable, "The SurveyJS service has no AI provider configured")]
    [InlineData(502, """{"error":"EXTRACTION_FAILED"}""", HttpStatusCode.BadGateway, "Could not extract the response from the document")]
    [InlineData(200, """{"error":"schema is required"}""", HttpStatusCode.BadGateway, "SurveyJS service error")]
    [InlineData(200, """{"data":[1,2]}""", HttpStatusCode.BadGateway, "SurveyJS service error")]
    [InlineData(422, """{"errors":[],"warnings":[]}""", HttpStatusCode.BadGateway, "SurveyJS service error")]
    public async Task ExtractMapsTheServiceAnswers(int status, string body, HttpStatusCode expected, string error)
    {
        _ = app.Server;
        Answers(status, body.Replace("[1,2]", $"[\"{Marker}\"]"));
        Assert.Equal(error, await (await Extract()).ErrorAsync(expected));
        Assert.DoesNotContain(app.Logs, line => line.Contains(Marker));
    }

    [Fact]
    public async Task ExtractChecksTheUploadBeforeCallingTheService()
    {
        _ = app.Server;
        Assert.Equal("Upload the scan in a field named \"scan\"", await (await Extract(field: "file")).ErrorAsync(HttpStatusCode.BadRequest));
        Assert.Equal("The file is not a PDF, PNG, JPEG, WebP or GIF document",
            await (await Extract("<html>not an image</html>"u8.ToArray())).ErrorAsync(HttpStatusCode.BadRequest));
        var big = new byte[5 * 1024 * 1024 + 1];
        Png.CopyTo(big, 0);
        Assert.Equal("The scan must be a file of 5 MB or less", await (await Extract(big)).ErrorAsync(HttpStatusCode.RequestEntityTooLarge));
        Assert.Empty(service.Calls);
    }
}
