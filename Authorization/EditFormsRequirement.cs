using Microsoft.AspNetCore.Authorization;

namespace SurveyJsAspNetCore.Authorization;

/// <summary>
/// The "edit-forms" policy: saving definitions (creator-load-save, lint-definition) and variable presets is an editor's
/// action, and so is joining the edit-together relay. Your real permission check goes here: roles, teams, form ownership…
/// The failure's message is the { error } of the 403, which Creator shows (creator-load-save).
/// </summary>
public sealed class EditFormsRequirement : AuthorizationHandler<EditFormsRequirement>, IAuthorizationRequirement
{
    public const string Policy = "edit-forms";

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, EditFormsRequirement requirement)
    {
        if (context.User.HasClaim("is_editor", "true"))
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, "You don't have editor rights: only editors can change forms"));
        }
        return Task.CompletedTask;
    }
}
