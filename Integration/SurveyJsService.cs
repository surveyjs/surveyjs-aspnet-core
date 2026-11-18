using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SurveyJsAspNetCore.Configuration;

namespace SurveyJsAspNetCore.Integration;

/// <summary>
/// What the SurveyJS service answered: the status, the content type and the bytes. HttpClient doesn't throw on 4xx or
/// 5xx, and the service answers some errors with 200, so the endpoints match the answers they recognize exactly.
/// </summary>
public sealed class ServiceAnswer(int status, string? contentType, byte[] body)
{
    public int Status { get; } = status;
    public string? ContentType { get; } = contentType;
    public byte[] Body { get; } = body;

    /// <summary>The body as text (JSON answers; never needed for a PDF).</summary>
    public string Text => Encoding.UTF8.GetString(Body);

    /// <summary>The body when it is a JSON object, otherwise null (an array, a PDF, malformed JSON).</summary>
    public JsonObject? Object => parsed.Value;

    /// <summary>The body's "error" when it is a string, as in { "error": "INVALID_DOCUMENT" }.</summary>
    public string? Error => Object?["error"] is JsonValue error && error.TryGetValue(out string? text) ? text : null;

    public bool HasContentType(string type) => ContentType?.StartsWith(type, StringComparison.OrdinalIgnoreCase) == true;

    public bool BodyStartsWith(string prefix) => Body.AsSpan().StartsWith(Encoding.ASCII.GetBytes(prefix));

    private readonly Lazy<JsonObject?> parsed = new(() =>
    {
        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    });
}

/// <summary>
/// Calls the SurveyJS service (SurveyJS Server, SURVEYJS_SERVICE_URL) for Run Server-Side Workflows: validate-response
/// validates, lint-definition lints, server-pdf prints a PDF and extract-from-paper extracts. A typed HttpClient from
/// IHttpClientFactory (Program.cs). When the service can't be reached, the request ends here: 504 when it timed out,
/// 503 when nothing listens. When you copy one of those examples, copy this file too.
/// </summary>
public sealed class SurveyJsService(HttpClient http, IOptions<SurveyJsOptions> options, IHttpContextAccessor context, ILogger<SurveyJsService> log)
{
    private string ServiceUrl => options.Value.ServiceUrl;

    /// <summary>POST JSON to the service with a timeout for this call (10 s /schema and /response, 60 s /pdf, 120 s /extract).</summary>
    public async Task<ServiceAnswer> PostAsync(string path, object? body, TimeSpan timeout)
    {
        var aborted = context.HttpContext?.RequestAborted ?? CancellationToken.None;
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        timer.CancelAfter(timeout);
        using var content = new StringContent(JsonSerializer.Serialize(body, StoredJson.Options), Encoding.UTF8, "application/json");
        try
        {
            using var answer = await http.PostAsync(ServiceUrl + path, content, timer.Token);
            var bytes = await answer.Content.ReadAsByteArrayAsync(timer.Token);
            return new ServiceAnswer((int)answer.StatusCode, answer.Content.Headers.ContentType?.ToString(), bytes);
        }
        catch (OperationCanceledException) when (!aborted.IsCancellationRequested)
        {
            log.LogError("SurveyJS service: {Path} timed out after {Timeout}", path, timeout);
            throw new ErrorResponseException(StatusCodes.Status504GatewayTimeout, "SurveyJS service timed out");
        }
        catch (HttpRequestException e) when (e.InnerException is SocketException)
        {
            log.LogError("SurveyJS service: nothing answers at {Url} ({Error})", ServiceUrl, e.InnerException.Message);
            throw new ErrorResponseException(StatusCodes.Status503ServiceUnavailable, $"SurveyJS service is not running at {ServiceUrl}");
        }
        catch (HttpRequestException e)
        {
            log.LogError(e, "SurveyJS service: {Path} failed", path);
            throw new ErrorResponseException(StatusCodes.Status502BadGateway, "SurveyJS service error");
        }
    }

    /// <summary>
    /// An answer the endpoint doesn't recognize: log it and end the request with 502 { error }. Fail closed.
    /// Extraction logs the status only (logBody: false), never the document's answers.
    /// </summary>
    public ErrorResponseException Unexpected(string path, ServiceAnswer answer, string message = "SurveyJS service error", bool logBody = true)
    {
        var body = !logBody ? "(not logged)"
            : answer.HasContentType("application/json") || answer.Object is not null ? Truncate(answer.Text, 2000)
            : $"({answer.ContentType ?? "no content type"}, {answer.Body.Length} bytes)";
        log.LogError("SurveyJS service: unexpected answer from {Path}: {Status} {Body}", path, answer.Status, body);
        return new ErrorResponseException(StatusCodes.Status502BadGateway, message);
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];
}
