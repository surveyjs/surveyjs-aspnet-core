using System.Text.Json;

namespace SurveyJsAspNetCore.Demo;

/// <summary>One example in surveyjs-integration.json.</summary>
public sealed record ManifestStep(
    string Slug, string Section, string Anchor, string Title, string Status, string Page, string Need, string Licence,
    List<string> Endpoints, Dictionary<string, string> Files, List<string> Regions, string? Note = null, string? See = null)
{
    public bool IsCommercial => Licence.StartsWith("Commercial", StringComparison.Ordinal);
    public bool NeedsAi => Licence.Contains("needs an AI provider", StringComparison.Ordinal);
}

/// <summary>Demo only: surveyjs-integration.json, which the catalog, the step pages and the website read.</summary>
public sealed record Manifest(
    string Contract, string Platform, string Framework, string Repo, string Branch, string? Live, string SurveyjsVersion,
    string ServerSideWorkflows, string? SurveyjsService, List<ManifestStep> Steps)
{
    /// <summary>The app title, where "SurveyJS" is already beside it: the catalog's browser title and the step pages' header.</summary>
    public const string Title = "ASP.NET Core Backend Integration";

    /// <summary>The catalog header, next to the logo.</summary>
    public const string Header = "Step-by-Step ASP.NET Core Backend Integration";

    public ManifestStep? Step(string slug) => Steps.FirstOrDefault(s => s.Slug == slug);

    public static Manifest Load(string contentRoot) =>
        JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(contentRoot, "surveyjs-integration.json")), JsonSerializerOptions.Web)
        ?? throw new InvalidDataException("surveyjs-integration.json is empty");
}
