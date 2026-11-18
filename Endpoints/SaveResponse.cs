using System.Text.Json.Nodes;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>Save Responses: POST /api/responses. ValidateResponse.cs replaces it when VALIDATE_RESPONSES=true.</summary>
public static class SaveResponse
{
    public static IEndpointRouteBuilder MapSaveResponse(this IEndpointRouteBuilder app)
    {
        #region sjs:save-response.server
        // POST /api/responses: Store submitted answers as one JSON document
        app.MapPost("/api/responses", async (HttpRequest request, SurveyDb db) =>
        {
            // Read the raw request body: binding to a class would drop unknown keys and re-format numbers such as 1.0
            if (await StoredJson.ReadBodyAsync(request) is not JsonObject body
                || body["formId"] is not JsonValue formId || body["data"] is not JsonNode data)
            {
                return Results.Json(new { error = "Expected { formId, data }" }, statusCode: 400);
            }

            var id = await StoredJson.InsertResponseAsync(db, formId.ToString(), data);   // Preserve float values such as 1.0

            return Results.Json(new { id }, statusCode: 201);
        });
        #endregion
        return app;
    }
}
