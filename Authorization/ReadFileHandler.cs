using Microsoft.AspNetCore.Authorization;
using SurveyJsAspNetCore.Data;

namespace SurveyJsAspNetCore.Authorization;

/// <summary>The "read-file" policy: a resource-based check before GET /api/files/{id} returns a private file.</summary>
public sealed class ReadFileRequirement : IAuthorizationRequirement
{
    public const string Policy = "read-file";
}

/// <summary>
/// Any signed-in user may read a stored file. Real apps check ownership here, for example file.OwnerId == the user's id.
/// store-files answers 404 when this denies access, so a denial doesn't confirm that the file exists.
/// </summary>
public sealed class ReadFileHandler : AuthorizationHandler<ReadFileRequirement, StoredFile>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ReadFileRequirement requirement, StoredFile file)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
