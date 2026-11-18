using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>
/// Validate Responses: POST /api/responses, checked by the SurveyJS service before it is stored. Program.cs maps it in
/// place of SaveResponse.cs when VALIDATE_RESPONSES=true. The service is SurveyJS Server: see docker-compose.yml.
/// </summary>
public static class ValidateResponse
{
    public static IEndpointRouteBuilder MapValidateResponse(this IEndpointRouteBuilder app)
    {
        #region sjs:validate-response.server
        // POST /api/responses — ask the SurveyJS service to validate, then store as usual
        app.MapPost("/api/responses", async (HttpRequest request, SurveyDb db, SurveyJsService service) =>
        {
            // The raw body keeps {} as {} and 1.0 as 1.0 (see Save Responses)
            if (await StoredJson.ReadBodyAsync(request) is not JsonObject body || body["data"] is not JsonObject data
                || body["formId"] is not JsonValue formIdValue || !formIdValue.TryGetValue(out string? formId))
                return Results.Json(new { error = "Expected { formId, data } with an answers object" }, statusCode: 400);
            var definition = await db.Forms.Where(f => f.Key == formId).Select(f => f.Json).SingleOrDefaultAsync();
            if (definition is null) return Results.Json(new { error = "Unknown form" }, statusCode: 404);

            var check = await service.PostAsync("/response", new { schema = JsonNode.Parse(definition), response = data }, TimeSpan.FromSeconds(10));
            if (check is { Status: 422, Object: { } found } && found["errors"] is not null && !found.ContainsKey("warnings"))
                return Results.Json(new { errors = found["errors"] }, statusCode: 400);   // the answers don't fit the definition
            if (check is not { Status: 200, Object.Count: 0 })                           // fail closed: only exactly {} means valid
                throw service.Unexpected("/response", check, check is { Status: 422, Object: { } invalid } && invalid.ContainsKey("errors")
                    && invalid.ContainsKey("warnings") ? "The stored definition has errors" : "SurveyJS service error");

            var id = await StoredJson.InsertResponseAsync(db, formId, data);              // stored as it arrived: 1.0 stays 1.0
            return Results.Json(new { id }, statusCode: 201);
        });
        #endregion
        return app;
    }
}
