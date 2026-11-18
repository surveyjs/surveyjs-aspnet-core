using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SurveyJsAspNetCore.Demo;

/// <summary>
/// Demo only: the import map and the SurveyJS stylesheets every example page loads, built once at start.
///
/// The shared ES modules (shared/client/*.js, shared/ui/*.js) import bare specifiers ("survey-core", "lodash/debounce").
/// SurveyJS comes from the NuGet packages: ASP.NET Core serves their ES module builds as static web assets under
/// /_content/SurveyJS.*/, with npm's paths. The third-party modules, which no SurveyJS package carries, come from unpkg
/// at the pinned versions below. Don't mix these ES modules with the packages' script-tag (UMD) builds on one page.
/// </summary>
public sealed partial class ImportMap
{
    /// <summary>Every SurveyJS specifier the shared modules and the SurveyJS builds import: NuGet package and file.</summary>
    private static readonly (string Specifier, string Package, string File)[] SurveyJs =
    [
        ("survey-core", "SurveyJS.Core", "fesm/survey-core.mjs"),
        ("survey-core/collaboration", "SurveyJS.Core", "fesm/collaboration/index.mjs"),
        ("survey-core/i18n", "SurveyJS.Core", "fesm/i18n/index.mjs"),
        ("survey-js-ui", "SurveyJS.JsUi", "fesm/survey-js-ui.mjs"),
        ("survey-creator-core", "SurveyJS.Creator.Core", "fesm/survey-creator-core.mjs"),
        ("survey-creator-core/collaboration", "SurveyJS.Creator.Core", "fesm/collaboration.mjs"),
        ("survey-creator-js", "SurveyJS.Creator.JsUi", "fesm/survey-creator-js.mjs"),
        ("survey-analytics", "SurveyJS.Analytics", "fesm/survey.analytics.mjs"),
    ];

    private const string Unpkg = "https://unpkg.com";

    /// <summary>highlight.js's own es/ files re-export its CommonJS lib/ (they are for bundlers), so a browser gets no default
    /// export: @highlightjs/cdn-assets is the same release built as browser ES modules.</summary>
    private const string Highlight = Unpkg + "/@highlightjs/cdn-assets@11.12.0";

    /// <summary>survey-analytics imports chart.js (^4.5.1), which imports @kurkle/color: the versions the reference's lock file has.</summary>
    private static readonly (string Specifier, string Url)[] ThirdParty =
    [
        ("@kurkle/color", Unpkg + "/@kurkle/color@0.3.4/dist/color.esm.js"),
        ("chart.js", Unpkg + "/chart.js@4.5.1/dist/chart.js"),
        ("highlight.js/lib/core", Highlight + "/es/core.min.js"),
        ("lodash/debounce", Unpkg + "/lodash-es@4.18.1/debounce.js"),
    ];

    public const string HighlightTheme = Highlight + "/styles/github-dark.min.css";

    public static readonly string[] Fonts = ["Regular", "Medium", "Semibold", "Bold"];

    /// <summary>{ "imports": { specifier: url } }, written into each example page as &lt;script type="importmap"&gt;.</summary>
    public JsonObject Map { get; }

    /// <summary>The SurveyJS stylesheets of a page theme (survey, creator or dashboard), in the order the pages need them.</summary>
    public IReadOnlyDictionary<string, string[]> Stylesheets { get; }

    public ImportMap(IWebHostEnvironment env)
    {
        var imports = new JsonObject();
        foreach (var (specifier, package, file) in SurveyJs)
        {
            imports[specifier] = Content(env, package, file);
        }
        foreach (var (specifier, url) in ThirdParty)
        {
            imports[specifier] = url;
        }
        // One entry per language shared/ui/code-highlight.js imports, so a language added there can't go unmapped
        var codeHighlight = File.ReadAllText(Path.Combine(env.ContentRootPath, "shared", "ui", "code-highlight.js"));
        foreach (Match language in Language().Matches(codeHighlight))
        {
            imports["highlight.js/lib/languages/" + language.Groups[1].Value] = $"{Highlight}/es/languages/{language.Groups[1].Value}.min.js";
        }
        Map = new JsonObject { ["imports"] = imports };

        var core = Content(env, "SurveyJS.Core", "survey-core.min.css");
        var adapter = Content(env, "SurveyJS.Core", "themes/adapters/tailwind4.min.css");
        Stylesheets = new Dictionary<string, string[]>
        {
            ["survey"] = [core, Content(env, "SurveyJS.Core", "collaboration.min.css"), adapter, "/shared/ui/survey-overrides.css"],
            ["creator"] = [core, Content(env, "SurveyJS.Creator.Core", "survey-creator-core.min.css"),
                           Content(env, "SurveyJS.Creator.Core", "collaboration.min.css"), adapter],
            ["dashboard"] = [core, Content(env, "SurveyJS.Analytics", "survey.analytics.min.css"), adapter],
        };
    }

    /// <summary>
    /// The URL of a file in a SurveyJS package's static web assets. Fails at start when the package doesn't have it,
    /// which is what a SurveyJS version without the ES module builds (3.2.5 and earlier) looks like.
    /// </summary>
    private static string Content(IWebHostEnvironment env, string package, string file)
    {
        var path = $"_content/{package}/{file}";
        if (!env.WebRootFileProvider.GetFileInfo(path).Exists)
        {
            throw new InvalidOperationException(
                $"The NuGet package {package} has no {file} (/{path}). The pages need the SurveyJS ES module builds, " +
                "which the SurveyJS NuGet packages ship from 3.2.6: set the SurveyJS package versions to shared/surveyjs-version.json.");
        }
        return "/" + path;
    }

    [GeneratedRegex(@"from\s+['""]highlight\.js/lib/languages/([a-z0-9-]+)['""]")]
    private static partial Regex Language();
}
