using System.Text.Json.Nodes;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>Use Relational Storage: POST /api/claims stores the original JSON, then copies answers into columns.</summary>
public static class RelationalStorage
{
    public static IEndpointRouteBuilder MapRelationalStorage(this IEndpointRouteBuilder app)
    {
        #region sjs:relational-storage.server
        // POST /api/claims — original JSON first, then the columns you query
        app.MapPost("/api/claims", async (HttpRequest request, SurveyDb db) =>
        {
            // Read the raw request body: binding to a class would drop unknown keys and re-format numbers in the stored JSON
            if (await StoredJson.ReadBodyAsync(request) is not JsonObject body
                || body["data"] is not JsonNode data || body["definitionVersion"] is not JsonValue version)
            {
                return Results.Json(new { error = "Expected { data, definitionVersion }" }, statusCode: 400);
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            var responseId = await StoredJson.InsertResponseAsync(db, "claim", data,   // the original, stored as it arrived: 1.0 stays 1.0
                definitionVersion: version.ToString());                               // keep the original and its version
            db.Claims.Add(new StoredClaim                                                 // keys come from valueName
            {
                ResponseId = responseId,
                CustomerEmail = (string?)data["customer_email"],
                Amount = (decimal?)data["amount"],
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Json(new { id = responseId }, statusCode: 201);
        });
        #endregion
        return app;
    }
}
