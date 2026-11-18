using System.Security.Claims;
using System.Text.Json.Nodes;

namespace SurveyJsAspNetCore.Pages.Examples;

/// <summary>
/// Load JSON Schemas, Variables, and Answers. This page serves /examples/load-definition-variables-data, which loads
/// the form from the API (Option B, Endpoints/LoadDefinitionVariablesData.cs), and /claim, which embeds it (Option A).
/// </summary>
public class LoadDefinitionVariablesDataModel(IWebHostEnvironment env) : ExampleModel
{
    /// <summary>Option A only: written into the page as window.FORM_DEFINITION and window.FORM_VARIABLES.</summary>
    public JsonNode? Definition { get; private set; }
    public object? Variables { get; private set; }

    public bool OptionA => Request.Path.StartsWithSegments("/claim");

    public void OnGet()
    {
        if (OptionA)
        {
            Embed();
        }
    }

    #region sjs:load-definition-variables-data.embed
    // Option A: Embed the schema and user variables in the HTML response
    private void Embed()
    {
        var path = Path.Combine(env.ContentRootPath, "shared", "definitions", "load-definition-variables-data.json");
        Definition = JsonNode.Parse(System.IO.File.ReadAllText(path));
        Variables = new { user = new { name = User.Identity?.Name, plan = User.FindFirstValue("plan") } };
    }   // The page writes both with @Json.Serialize(), which escapes them for an inline script
    #endregion
}
