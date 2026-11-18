using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SurveyJsAspNetCore.Configuration;
using SurveyJsAspNetCore.Data;

namespace SurveyJsAspNetCore.Demo;

/// <summary>Demo only: window.SURVEYJS_PAGE, what shared/client/host.js and the step modules read.</summary>
public static class PageConfig
{
    public static async Task<Dictionary<string, object?>> BuildAsync(ManifestStep step, Manifest manifest, SurveyJsOptions options,
        HttpContext context, SurveyDb db, RequestStorage storage)
    {
        var user = context.User;
        var key = DemoUsers.Key(user);
        var config = new Dictionary<string, object?>
        {
            ["slug"] = step.Slug,
            ["version"] = manifest.SurveyjsVersion,
            ["licenseKey"] = options.LicenseKey,
            ["demoUser"] = key,
            ["user"] = user.Identity?.IsAuthenticated == true
                ? new { key, name = user.Identity.Name, plan = user.FindFirstValue(DemoUsers.PlanClaim), isEditor = user.HasClaim(DemoUsers.EditorClaim, "true") }
                : null,
            ["relay"] = new
            {
                fill = new { url = options.RelayUrl ?? Origin(context), port = options.AppPort },
                edit = new { url = options.EditRelayUrl ?? Origin(context), port = options.AppPort },
            },
            ["demoMode"] = options.DemoMode,
            ["sandboxId"] = storage.SandboxId,
            ["switches"] = new { validateResponses = options.ValidateResponses, lintDefinitions = options.LintDefinitions },
        };
        foreach (var (name, path) in step.Files.Where(f => f.Key.StartsWith("definition", StringComparison.Ordinal)))
        {
            config[name] = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ContentRoot(context), path)));
        }
        if (step.Slug == "extract-from-paper")
        {
            // The blank PDF, the extraction and the review form all use the stored work-order form
            var definition = await db.Forms.Where(f => f.Key == "work-order").Select(f => f.Json).SingleOrDefaultAsync();
            config["definition"] = definition is null ? null : JsonNode.Parse(definition);
        }
        return config;
    }

    /// <summary>The relays run in this app: the page's own origin, ws or wss by the scheme the visitor sees.</summary>
    public static string Origin(HttpContext context) =>
        $"{(context.Request.IsHttps ? "wss" : "ws")}://{context.Request.Host}";

    private static string ContentRoot(HttpContext context) =>
        context.RequestServices.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
}
