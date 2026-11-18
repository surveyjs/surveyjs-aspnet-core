using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SurveyJsAspNetCore.Configuration;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Demo;

namespace SurveyJsAspNetCore.Pages.Examples;

/// <summary>
/// Demo only: what every example page shows around the component (Pages/Shared/_ExampleLayout.cshtml). The page's file
/// name gives its slug (SaveResponse.cshtml is save-response). Before the page runs, it signs the request in as the
/// page's demo user and builds window.SURVEYJS_PAGE.
/// </summary>
public partial class ExampleModel : PageModel
{
    public ManifestStep Step { get; private set; } = null!;
    public Manifest Manifest { get; private set; } = null!;
    public SurveyJsOptions Settings { get; private set; } = null!;
    public Dictionary<string, object?> Config { get; private set; } = [];

    /// <summary>The users the page's switch offers; one means no switch.</summary>
    public string[] Users => DemoUsers.ForPage(Step.Slug);

    protected SurveyDb Db => HttpContext.RequestServices.GetRequiredService<SurveyDb>();

    /// <summary>The page's slug, from its file name: /Examples/SaveResponse is save-response.</summary>
    public string Slug => Kebab().Replace(PageContext.ActionDescriptor.ViewEnginePath.Split('/')[^1], "-$1").ToLowerInvariant();

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var services = HttpContext.RequestServices;
        Manifest = services.GetRequiredService<Manifest>();
        Settings = services.GetRequiredService<IOptions<SurveyJsOptions>>().Value;
        Step = Manifest.Step(Slug) ?? throw new InvalidOperationException($"surveyjs-integration.json has no step {Slug}");
        await DemoUsers.SignInForPageAsync(Step.Slug, HttpContext);
        Config = await PageConfig.BuildAsync(Step, Manifest, Settings, HttpContext, Db, services.GetRequiredService<RequestStorage>());
        await next();
    }

    /// <summary>Stored responses to pick from on a page: id => "#id · label".</summary>
    public async Task<Dictionary<long, string>> RecordsAsync(string formId, string labelKey, int limit = 10, string? customerEmail = null)
    {
        var rows = Db.Responses.Where(r => r.FormId == formId);
        if (customerEmail is not null) rows = rows.Where(r => SurveyDb.JsonValue(r.Data, "$.customer_email") == customerEmail);
        var list = await rows.OrderBy(r => r.Id).Take(limit).Select(r => new { r.Id, r.Data }).ToListAsync();
        return list.ToDictionary(r => r.Id, r => $"#{r.Id} · {Label(JsonNode.Parse(r.Data)?[labelKey])}");
    }

    /// <summary>The forms of the collaborative editing demo (*-team, team-*), by key, with their titles.</summary>
    public async Task<Dictionary<string, string>> CollaborativeFormsAsync()
    {
        var rows = await Db.Forms.Where(f => EF.Functions.Like(f.Key, "%-team") || EF.Functions.Like(f.Key, "team-%"))
            .OrderBy(f => f.Key).Select(f => new { f.Key, f.Json }).ToListAsync();
        return rows.ToDictionary(r => r.Key, r =>
        {
            var title = JsonNode.Parse(r.Json)?["title"];
            var label = title is JsonObject localized ? Label(localized["default"]) : Label(title);
            return label is "" ? "Untitled form" : label;
        });
    }

    private static string Label(JsonNode? value) => value is JsonValue text && text.TryGetValue(out string? s) ? s : value?.ToJsonString() ?? "";

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex Kebab();
}
