using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>
/// Load and Save Form JSON Schemas: PUT /api/forms/{id}. GET /api/forms/{id} is in LoadDefinitionVariablesData.cs:
/// Creator reads its .definition. This file holds only the PUT, so LINT_DEFINITIONS=true can swap in LintDefinition.cs.
/// </summary>
public static class CreatorLoadSave
{
    public static IEndpointRouteBuilder MapCreatorLoadSave(this IEndpointRouteBuilder app)
    {
        #region sjs:creator-load-save.server
        // PUT /api/forms/{id} — store the definition as-is: it's one JSON document
        app.MapPut("/api/forms/{id}", async (string id, HttpRequest request, ClaimsPrincipal user, IAuthorizationService auth, SurveyDb db) =>
        {
            var allowed = await auth.AuthorizeAsync(user, "edit-forms");   // editing is an admin action: 403 otherwise
            if (!allowed.Succeeded)
                return Results.Json(new { error = allowed.Failure?.FailureReasons.FirstOrDefault()?.Message }, statusCode: 403);
            // The raw body, not a bound class: binding would drop the keys the class doesn't know and re-format numbers
            var definition = await StoredJson.ReadBodyAsync(request);
            await StoredJson.UpsertAsync(db, StoredTable.Forms, id, definition);   // stored as it arrived: 1.0 stays 1.0

            return Results.NoContent();   // the next page load renders the new version
        });
        #endregion
        return app;
    }
}
