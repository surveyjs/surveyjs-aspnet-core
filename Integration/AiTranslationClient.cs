using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SurveyJsAspNetCore.Configuration;

namespace SurveyJsAspNetCore.Integration;

/// <summary>
/// Asks any OpenAI-compatible /chat/completions endpoint (AI_BASE_URL, AI_API_KEY, AI_MODEL) to translate Creator's
/// strings. Your provider and your key: nothing goes to SurveyJS. Swap this file for your provider's SDK if you like;
/// the endpoint only needs an array back. When you copy ai-translation, copy this file too.
/// </summary>
public sealed partial class AiTranslationClient(HttpClient http, IOptions<SurveyJsOptions> options, ILogger<AiTranslationClient> log)
{
    /// <summary>The translated strings as the model returned them (a JSON array if all went well), or null.</summary>
    public async Task<JsonNode?> TranslateAsync(IReadOnlyList<string> strings, string from, string to, CancellationToken aborted)
    {
        var ai = options.Value;
        var request = new
        {
            model = ai.AiModel,
            messages = new object[]
            {
                new { role = "system", content = $"Translate each string from {from} to {to}. Return a JSON array in the same order." },
                new { role = "user", content = JsonSerializer.Serialize(strings, StoredJson.Options) },
            },
        };
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        timer.CancelAfter(TimeSpan.FromSeconds(60));
        using var message = new HttpRequestMessage(HttpMethod.Post, ai.AiBaseUrl + "/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(request, StoredJson.Options), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ai.AiApiKey);
        string? content;
        try
        {
            using var reply = await http.SendAsync(message, timer.Token);
            reply.EnsureSuccessStatusCode();
            var answer = JsonNode.Parse(await reply.Content.ReadAsStringAsync(timer.Token));
            content = answer?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException
                                  && !aborted.IsCancellationRequested)
        {
            log.LogError(e, "AI translation: the provider at {Url} failed", ai.AiBaseUrl);
            throw new ErrorResponseException(StatusCodes.Status502BadGateway, "The translation provider is unavailable. Please try again.");
        }

        // Models sometimes wrap the array in prose or a code fence: take the array itself
        var array = ArrayInText().Match(content ?? "");
        try
        {
            return array.Success ? JsonNode.Parse(array.Value) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\[.*\]", RegexOptions.Singleline)]
    private static partial Regex ArrayInText();
}
