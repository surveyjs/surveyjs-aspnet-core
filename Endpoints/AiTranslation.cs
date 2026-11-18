using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SurveyJsAspNetCore.Configuration;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>Translate with AI: POST /api/translate, through your own AI provider (Integration/AiTranslationClient.cs).</summary>
public static class AiTranslation
{
    /// <summary>What Creator's onMachineTranslate sends.</summary>
    public sealed record TranslateRequest(List<string> Strings, string From, string To);

    public static IEndpointRouteBuilder MapAiTranslation(this IEndpointRouteBuilder app)
    {
        #region sjs:ai-translation.server
        // POST /api/translate — strings in, translated strings out (same order); nothing goes to SurveyJS
        app.MapPost("/api/translate", async (TranslateRequest body, AiTranslationClient ai, IOptions<SurveyJsOptions> options, CancellationToken aborted) =>
        {
            if (string.IsNullOrEmpty(options.Value.AiApiKey))
                return Results.Json(new { error = "Set AI_API_KEY to enable translation" }, statusCode: 501);

            var translated = await ai.TranslateAsync(body.Strings, body.From, body.To, aborted);   // any OpenAI-compatible provider: your key, your choice
            if (translated is not JsonArray list || list.Count != body.Strings.Count || list.Any(s => s?.GetValueKind() != JsonValueKind.String))
                return Results.Json(new { error = "The AI provider returned an unexpected answer" }, statusCode: 502);

            return Results.Json(list);   // what options.callback() takes
        });
        #endregion
        return app;
    }
}
