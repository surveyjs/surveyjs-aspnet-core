using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SurveyJsAspNetCore.Data;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>
/// Load JSON Schemas, Variables, and Answers: Option B, GET /api/forms/{id}. Option A (GET /claim) is a Razor page:
/// Pages/Examples/LoadDefinitionVariablesData.cshtml.cs. Creator and the Dashboard read this endpoint's .definition.
/// </summary>
public static class LoadDefinitionVariablesData
{
    public static IEndpointRouteBuilder MapLoadDefinitionVariablesData(this IEndpointRouteBuilder app)
    {
        #region sjs:load-definition-variables-data.server
        // Option B: Return the definition, variables, and previous answers in one response
        app.MapGet("/api/forms/{id}", async (string id, string? record, ClaimsPrincipal user, SurveyDb db) =>
        {
            var definition = await db.Forms.Where(f => f.Key == id).Select(f => f.Json).SingleOrDefaultAsync();
            if (definition is null) return Results.Json(new { error = "Unknown form" }, statusCode: 404);

            var email = user.FindFirstValue(ClaimTypes.Email);
            long? recordId = long.TryParse(record, out var parsed) ? parsed : null;   // ?record= is empty for a new response
            var records = db.Responses.Where(r => r.Id == recordId);
            if (id == "claim-request")   // Only the signed-in customer's own claims
                records = records.Where(r => r.FormId == "claim" && SurveyDb.JsonValue(r.Data, "$.customer_email") == email);
            var data = recordId is null ? null : await records.Select(r => r.Data).SingleOrDefaultAsync();

            return Results.Json(new
            {
                definition = JsonNode.Parse(definition),                          // Return the stored form definition
                variables = new { user = new { name = user.Identity?.Name, plan = user.FindFirstValue("plan") } },
                data = data is null ? new JsonObject() : JsonNode.Parse(data),   // Return previous answers, or {} when none are available
            });
        });
        #endregion
        return app;
    }
}
