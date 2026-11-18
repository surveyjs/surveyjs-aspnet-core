using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>Lint JSON Schemas: PUT /api/forms/{id}, linted first. Program.cs maps it in place of CreatorLoadSave.cs when LINT_DEFINITIONS=true.</summary>
public static class LintDefinition
{
    public static IEndpointRouteBuilder MapLintDefinition(this IEndpointRouteBuilder app)
    {
        #region sjs:lint-definition.server
        // PUT /api/forms/{id} — lint the definition from Creator before storing it
        app.MapPut("/api/forms/{id}", async (string id, HttpRequest request, ClaimsPrincipal user, IAuthorizationService auth, SurveyDb db, SurveyJsService service) =>
        {
            var allowed = await auth.AuthorizeAsync(user, "edit-forms");
            if (!allowed.Succeeded)
                return Results.Json(new { error = allowed.Failure?.FailureReasons.FirstOrDefault()?.Message }, statusCode: 403);
            var definition = await StoredJson.ReadBodyAsync(request);   // the raw body keeps {} as {} (see Load and Save Form JSON Schemas)

            var lint = await service.PostAsync("/schema", definition, TimeSpan.FromSeconds(10));
            if (lint is { Status: 422, Object: { } found } && found.ContainsKey("errors") && found.ContainsKey("warnings"))
                return Results.Text(lint.Text, "application/json", statusCode: 422);      // errors: refuse the save, report them as they are
            var warningsOnly = lint is { Status: 200, Object: { Count: 1 } answer } && answer.ContainsKey("warnings");
            if (!warningsOnly && lint is not { Status: 200, Object.Count: 0 })           // fail closed: nothing else counts as clean
                throw service.Unexpected("/schema", lint);
            await StoredJson.UpsertAsync(db, StoredTable.Forms, id, definition);         // stored as it arrived: 1.0 stays 1.0

            return warningsOnly ? Results.Json(new { warnings = lint.Object!["warnings"] }) : Results.NoContent();   // warnings don't block the save
        });
        #endregion
        return app;
    }
}
