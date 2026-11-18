using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>Resume Progress: GET, PUT and DELETE /api/progress/{formId}, one draft per user and form.</summary>
public static class ResumeProgress
{
    public static IEndpointRouteBuilder MapResumeProgress(this IEndpointRouteBuilder app)
    {
        #region sjs:resume-progress.server
        // Return the current user's saved answers and UI state
        app.MapGet("/api/progress/{formId}", async (string formId, ClaimsPrincipal user, SurveyDb db) =>
        {
            if (user.FindFirstValue(ClaimTypes.NameIdentifier) is not { } userId)
                return Results.Json(new { error = "Sign in to load progress" }, statusCode: 401);
            var key = $"{userId}:{formId}";
            var json = await db.Progress.Where(p => p.Key == key).Select(p => p.Json).SingleOrDefaultAsync();

            return Results.Text(json ?? "null", "application/json");   // the draft as stored, or null when there is none
        });

        // Store progress under a key containing both the user ID and form ID
        app.MapPut("/api/progress/{formId}", async (string formId, HttpRequest request, ClaimsPrincipal user, SurveyDb db) =>
        {
            if (user.FindFirstValue(ClaimTypes.NameIdentifier) is not { } userId)
                return Results.Json(new { error = "Sign in to save progress" }, statusCode: 401);
            // Read the raw request body: binding to a class would drop unknown keys and re-format numbers in the stored JSON
            var progress = await StoredJson.ReadBodyAsync(request);
            await StoredJson.UpsertAsync(db, StoredTable.Progress, $"{userId}:{formId}", progress);   // Preserve float values such as 1.0

            return Results.NoContent();
        });
        #endregion

        #region sjs:resume-progress.delete
        // Remove only the current user's draft
        app.MapDelete("/api/progress/{formId}", async (string formId, ClaimsPrincipal user, SurveyDb db) =>
        {
            if (user.FindFirstValue(ClaimTypes.NameIdentifier) is not { } userId)
                return Results.Json(new { error = "Sign in to clear progress" }, statusCode: 401);
            var key = $"{userId}:{formId}";
            await db.Progress.Where(p => p.Key == key).ExecuteDeleteAsync();

            return Results.NoContent();
        });
        #endregion
        return app;
    }
}
