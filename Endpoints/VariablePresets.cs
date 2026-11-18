using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>Variable Presets: GET and PUT /api/variable-presets/{formId}, the Preview tab's preset list.</summary>
public static class VariablePresets
{
    public static IEndpointRouteBuilder MapVariablePresets(this IEndpointRouteBuilder app)
    {
        #region sjs:variable-presets.server
        // GET /api/variable-presets/{formId} — return the stored preset list, or [] when none are saved
        app.MapGet("/api/variable-presets/{formId}", async (string formId, SurveyDb db) =>
        {
            var json = await db.VariablePresets.Where(p => p.Key == formId).Select(p => p.Json).SingleOrDefaultAsync();
            return Results.Text(json ?? "[]", "application/json");
        });

        // PUT /api/variable-presets/{formId} — store the variable presets as-is: it's one JSON document
        app.MapPut("/api/variable-presets/{formId}", async (string formId, HttpRequest request, ClaimsPrincipal user, IAuthorizationService auth, SurveyDb db) =>
        {
            var allowed = await auth.AuthorizeAsync(user, "edit-forms");   // editing is an admin action: 403 otherwise
            if (!allowed.Succeeded)
                return Results.Json(new { error = allowed.Failure?.FailureReasons.FirstOrDefault()?.Message }, statusCode: 403);
            // The raw body, not a bound class: binding would drop the keys the class doesn't know and re-format numbers
            var presets = await StoredJson.ReadBodyAsync(request);
            await StoredJson.UpsertAsync(db, StoredTable.VariablePresets, formId, presets);   // stored as it arrived: 1.0 stays 1.0

            return Results.NoContent();
        });
        #endregion
        return app;
    }
}
