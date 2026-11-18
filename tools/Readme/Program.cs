// dotnet run --project tools/Readme: regenerates the README's example table (between the steps markers) from
// surveyjs-integration.json.
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

const string docs = "https://surveyjs.io/documentation/backend-integration-aspnet";
var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "surveyjs-integration.json")))
{
    root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("Run this from the repository");
}

var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "surveyjs-integration.json")))!;
var live = (string?)manifest["live"];
var rows = new List<string> { "| Example | Run | Server | Client | Page | Licence |", "|---|---|---|---|---|---|" };
foreach (var step in manifest["steps"]!.AsArray())
{
    var page = (string)step!["page"]!;
    var run = live is null ? $"`{page}`" : $"[{page}]({live}{page})";
    rows.Add($"| [{step["title"]}]({docs}#{step["anchor"]}) | {run} | {Link(step, "server")} | {Link(step, "client")} | {Link(step, "page")} | {step["licence"]} |");
}

var path = Path.Combine(root, "README.md");
var readme = File.ReadAllText(path);
var markers = Regex.Match(readme, @"(<!-- steps:start[^>]*-->)[\s\S]*?(<!-- steps:end -->)");
if (!markers.Success)
{
    Console.Error.WriteLine("README.md needs <!-- steps:start --> and <!-- steps:end --> markers.");
    return 1;
}
var table = markers.Groups[1].Value + "\n" + string.Join("\n", rows) + "\n" + markers.Groups[2].Value;
File.WriteAllText(path, readme[..markers.Index] + table + readme[(markers.Index + markers.Length)..]);
Console.WriteLine($"Updated the example table in README.md ({rows.Count - 2} examples).");
return 0;

static string Link(JsonNode step, string role) =>
    step["files"]?[role] is JsonValue value ? $"[`{value}`]({value})" : "—";
