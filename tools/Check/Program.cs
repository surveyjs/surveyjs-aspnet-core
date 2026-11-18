// dotnet run --project tools/Check -- [--complete]: does this repo still match the integration docs (server-integration-v4)?
//
// The manifest's files and regions, the region rules (no nesting, short, no plumbing in server regions), shared/ against
// its SHA256SUMS, the SurveyJS pins against shared/surveyjs-version.json, the service revision against docker-compose.yml,
// the project's package references, the dotnet-ef pin, no *Raw SQL, and the README's docs links. With --complete (CI, the
// end of a run) all 18 examples must be in the manifest. Exit code 1 on any finding.
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SurveyJsAspNetCore.Demo;

var complete = args.Contains("--complete");
var root = FindRoot();
var errors = new List<string>();

string[] steps =
[
    "save-response", "load-definition-variables-data", "resume-progress", "store-files", "choices-from-web", "async-functions",
    "relational-storage", "fill-together", "dashboard", "creator-load-save", "creator-uploads", "ai-translation", "variable-presets",
    "edit-together", "validate-response", "lint-definition", "server-pdf", "extract-from-paper",
];
string[] regionSources = ["Endpoints", "Relay", "Pages/Examples", "shared/client"];
string[] relayFiles = ["Endpoints/FillTogether.cs", "Endpoints/EditTogether.cs"];
const int maxRegionLines = 24, maxRelayRegionLines = 60;
const string docs = "https://surveyjs.io/documentation/backend-integration-aspnet";
// This platform's plumbing: a server region calls Integration/ or Data/ for these instead of showing them
string[] plumbing = ["SqliteConnection", "ExecuteSql", "JsonSerializerOptions", "HttpRequestException", "TaskCanceledException", "CultureInfo"];
// EF Core's raw SQL methods take a string, so a value pasted into it is SQL: use the interpolated FromSql / ExecuteSql
string[] rawSql = ["FromSql" + "Raw", "ExecuteSql" + "Raw", "SqlQuery" + "Raw"];
// The only packages the web project may reference (README: Dependencies); Microsoft.Extensions.* come with the shared framework
string[] packages = ["Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Design", "SurveyJS", "SurveyJS.Creator", "SurveyJS.Analytics"];

// ---- shared/: CONTRACT, UPSTREAM and the hashes (shared/ is a copy: fixes go to surveyjs-php-laravel)
var contract = ReadOrEmpty("shared/CONTRACT").Trim();
if (contract != "server-integration-v4") errors.Add($"shared/CONTRACT must read server-integration-v4, found \"{contract}\"");
var upstream = ReadOrEmpty("shared/UPSTREAM").Trim();
if (!Regex.IsMatch(upstream, "^surveyjs/surveyjs-php-laravel@[0-9a-f]{7,40}$"))
    errors.Add($"shared/UPSTREAM must name the copied commit (surveyjs/surveyjs-php-laravel@<sha>), found \"{upstream}\"");
var expected = Read("shared/SHA256SUMS").Split('\n', StringSplitOptions.RemoveEmptyEntries)
    .ToDictionary(line => line[66..].Trim(), line => line[..64]);
var actual = Files("shared").Where(p => p is not "shared/SHA256SUMS" and not "shared/UPSTREAM")
    .ToDictionary(p => p["shared/".Length..], p => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, p)))));
foreach (var (path, digest) in expected)
{
    if (!actual.TryGetValue(path, out var found)) errors.Add($"shared/{path} is listed in shared/SHA256SUMS but missing");
    else if (found != digest) errors.Add($"shared/{path} differs from shared/SHA256SUMS: shared/ is a copy, fix it in surveyjs-php-laravel (on Windows, check that .gitattributes keeps LF line endings)");
}
foreach (var path in actual.Keys.Where(p => !expected.ContainsKey(p))) errors.Add($"shared/{path} is not listed in shared/SHA256SUMS");

// ---- versions: the manifest, shared/surveyjs-version.json, the NuGet references and package.json pin the same SurveyJS
var manifest = JsonNode.Parse(Read("surveyjs-integration.json"))!.AsObject();
var version = (string)JsonNode.Parse(Read("shared/surveyjs-version.json"))!["version"]!;
if ((string?)manifest["surveyjsVersion"] != version)
    errors.Add($"surveyjsVersion in the manifest is {manifest["surveyjsVersion"]}, shared/surveyjs-version.json says {version}");
var project = Read("surveyjs-aspnet-core.csproj");
var references = Regex.Matches(project, "<PackageReference\\s+Include=\"([^\"]+)\"\\s+Version=\"([^\"]+)\"")
    .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
foreach (var package in new[] { "SurveyJS", "SurveyJS.Creator", "SurveyJS.Analytics" })
{
    if (references.GetValueOrDefault(package) != version)
        errors.Add($"surveyjs-aspnet-core.csproj references {package} {references.GetValueOrDefault(package) ?? "(none)"}, expected {version} (shared/surveyjs-version.json)");
}
foreach (var package in references.Keys.Where(p => !packages.Contains(p)))
    errors.Add($"surveyjs-aspnet-core.csproj references {package}, which isn't one of the dependencies README lists");
var efCore = references.GetValueOrDefault("Microsoft.EntityFrameworkCore.Sqlite");
if (references.GetValueOrDefault("Microsoft.EntityFrameworkCore.Design") != efCore)
    errors.Add("Microsoft.EntityFrameworkCore.Design and .Sqlite must have the same version");
var tool = (string?)JsonNode.Parse(Read("dotnet-tools.json"))?["tools"]?["dotnet-ef"]?["version"];
if (tool != efCore) errors.Add($"dotnet-tools.json pins dotnet-ef {tool}, EF Core is {efCore}");
var packageJson = JsonNode.Parse(Read("package.json"))!;
if ((string?)packageJson["devDependencies"]?["survey-core"] != version)
    errors.Add($"package.json pins survey-core to {packageJson["devDependencies"]?["survey-core"]}, expected exactly {version} (the lint spec imports it)");
if (File.Exists(Path.Combine(root, "package-lock.json")))
{
    var locked = (string?)JsonNode.Parse(Read("package-lock.json"))?["packages"]?["node_modules/survey-core"]?["version"];
    if (locked != version) errors.Add($"package-lock.json has survey-core {locked}, expected {version}: run npm install");
}
if ((string?)manifest["serverSideWorkflows"] != "service") errors.Add($"serverSideWorkflows must be \"service\", found {manifest["serverSideWorkflows"]}");
var service = Regex.Match(ReadOrEmpty("docker-compose.yml"), "surveyjs-server\\.git#([0-9a-f]{40})");
if (!service.Success || (string?)manifest["surveyjsService"] != $"surveyjs/surveyjs-server@{service.Groups[1].Value}")
    errors.Add("surveyjsService in the manifest differs from the surveyjs build context in docker-compose.yml");

// ---- no raw SQL strings anywhere (the migrations are generated)
foreach (var path in Files(".").Where(p => p.EndsWith(".cs") && !p.StartsWith("Data/Migrations/") && !p.StartsWith("tools/Check/")))
{
    var text = Read(path);
    foreach (var method in rawSql.Where(text.Contains)) errors.Add($"{path} calls {method}: use the interpolated FromSql / ExecuteSql, which make every {{value}} a parameter");
}

// ---- the manifest's files
foreach (var step in manifest["steps"]!.AsArray())
{
    var slug = (string)step!["slug"]!;
    foreach (var (key, path) in step["files"]!.AsObject())
    {
        if (!File.Exists(Path.Combine(root, (string)path!))) errors.Add($"{slug}: {key} file {path} is missing");
    }
    if ((string?)step["status"] is not ("done" or "not-in-repo")) errors.Add($"{slug}: unknown status {step["status"]}");
}

// ---- regions: markers balanced, each region once, short, listed, and in its example's client or server files
var foundIn = new Dictionary<string, List<string>>();
foreach (var path in regionSources.SelectMany(Files).Where(p => p.EndsWith(".cs") || p.EndsWith(".js")))
{
    var lines = File.ReadAllLines(Path.Combine(root, path));
    var openAt = 0;
    for (var i = 0; i < lines.Length; i++)
    {
        if (Regex.IsMatch(lines[i], @"^\s*(//\s*)?#region\b"))
        {
            if (openAt > 0) errors.Add($"{path}:{i + 1}: a region opens inside another region");
            openAt = i + 1;
        }
        else if (Regex.IsMatch(lines[i], @"^\s*(//\s*)?#endregion\b"))
        {
            if (openAt == 0) errors.Add($"{path}:{i + 1}: endregion without region");
            openAt = 0;
        }
    }
    if (openAt > 0) errors.Add($"{path}:{openAt}: a region is never closed");
    foreach (var region in CodePanel.RegionsIn(root, path))
    {
        foundIn.TryAdd(region.Id, []);
        foundIn[region.Id].Add(path);
        var count = region.End - region.Start + 1;
        var limit = relayFiles.Contains(path) ? maxRelayRegionLines : maxRegionLines;
        if (count > limit) errors.Add($"Region sjs:{region.Id} in {path} has {count} lines (at most {limit})");
        if (path.EndsWith(".cs"))
        {
            foreach (var token in plumbing.Where(region.Code.Contains))
                errors.Add($"Region sjs:{region.Id} in {path} shows {token}: move it into Integration/ or Data/");
        }
    }
}
var listed = new HashSet<string>();
foreach (var step in manifest["steps"]!.AsArray())
{
    var sources = step!["files"]!.AsObject().Where(f => f.Key == "client" || f.Key.StartsWith("server")).Select(f => (string)f.Value!).ToList();
    foreach (var id in step["regions"]!.AsArray().Select(r => (string)r!))
    {
        listed.Add(id);
        var where = foundIn.GetValueOrDefault(id) ?? [];
        if (where.Count != 1) errors.Add($"{step["slug"]}: region sjs:{id} found {where.Count} times, expected exactly once");
        else if (!sources.Contains(where[0])) errors.Add($"{step["slug"]}: region sjs:{id} is in {where[0]}, not in the example's client or server file");
    }
}
foreach (var (id, where) in foundIn.Where(r => !listed.Contains(r.Key))) errors.Add($"Region sjs:{id} in {where[0]} is not listed in the manifest");

// ---- README: the prose links the docs page exactly twice (the generated table always holds it)
if (File.Exists(Path.Combine(root, "README.md")))
{
    var prose = Regex.Replace(Read("README.md"), "<!-- steps:start[\\s\\S]*?<!-- steps:end -->", "");
    var links = Regex.Matches(prose, @"\]\((https?://[^)\s]+)\)|<(https?://[^>\s]+)>")
        .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
        .Where(url => url.StartsWith(docs) || Regex.IsMatch(url, @"^https?://(www\.)?surveyjs\.io(/documentation)?/backend-integration"))
        .ToList();
    if (links.Count != 2 || links.Any(url => url != docs))
        errors.Add($"README.md must link to the docs exactly twice outside the example table, both to {docs}; found {links.Count}");
}
else
{
    errors.Add("README.md is missing");
}

if (complete)
{
    var slugs = manifest["steps"]!.AsArray().Select(s => (string)s!["slug"]!).ToHashSet();
    foreach (var slug in steps.Where(s => !slugs.Contains(s))) errors.Add($"Example {slug} is missing from surveyjs-integration.json");
}

foreach (var error in errors) Console.Error.WriteLine("ERROR  " + error);
if (errors.Count > 0)
{
    Console.Error.WriteLine($"{errors.Count} finding(s)");
    return 1;
}
Console.WriteLine($"OK: {manifest["steps"]!.AsArray().Count} examples, regions, shared/, versions and pins are consistent.");
return 0;

string Read(string path) => File.ReadAllText(Path.Combine(root, path));

string ReadOrEmpty(string path) => File.Exists(Path.Combine(root, path)) ? Read(path) : "";

IEnumerable<string> Files(string folder)
{
    string[] skipped = ["bin", "obj", "node_modules", ".git", "storage", "test-results", "playwright-report"];
    var found = new List<string>();
    var pending = new Stack<string>([Path.Combine(root, folder)]);
    while (pending.TryPop(out var dir))
    {
        if (!Directory.Exists(dir)) continue;
        found.AddRange(Directory.EnumerateFiles(dir).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')));
        foreach (var sub in Directory.EnumerateDirectories(dir).Where(d => !skipped.Contains(Path.GetFileName(d)))) pending.Push(sub);
    }
    return found.Order(StringComparer.Ordinal);
}

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "surveyjs-integration.json"))) return dir.FullName;
    }
    throw new InvalidOperationException("Run this from the repository (no surveyjs-integration.json found)");
}
