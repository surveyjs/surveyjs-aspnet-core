using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SurveyJsAspNetCore.Configuration;

namespace SurveyJsAspNetCore.Demo;

/// <summary>A <c>#region sjs:&lt;id&gt;</c> found in a file: its code (dedented) and the 1-based lines inside the markers.</summary>
public sealed record CodeRegion(string Id, string Path, int Start, int End, string Code);

/// <summary>One block of the Code tab.</summary>
public sealed record CodeBlock(string Role, string Path, string Language, string Code, string Url, string? Id = null);

/// <summary>
/// Demo only: the Code tab. It shows the files that just ran, cut by the same region markers the docs use:
/// <c>#region sjs:&lt;id&gt;</c> / <c>#endregion</c> in C#, <c>// #region sjs:&lt;id&gt;</c> / <c>// #endregion</c> in JavaScript.
/// </summary>
public sealed partial class CodePanel(Manifest manifest, IOptions<SurveyJsOptions> options, IWebHostEnvironment env)
{
    /// <summary>Every region in a file, by id (a duplicate id keeps the first; the check command reports duplicates).</summary>
    public static List<CodeRegion> RegionsIn(string root, string path)
    {
        var lines = File.ReadAllLines(System.IO.Path.Combine(root, path));
        var regions = new List<CodeRegion>();
        (string Id, int From)? open = null;
        for (var i = 0; i < lines.Length; i++)
        {
            if (RegionStart().Match(lines[i]) is { Success: true } start)
            {
                open = (start.Groups[1].Value, i + 1);
            }
            else if (open is { } region && RegionEnd().IsMatch(lines[i]))
            {
                regions.Add(new CodeRegion(region.Id, path, region.From + 1, i, Dedent(lines[region.From..i])));
                open = null;
            }
        }
        return regions;
    }

    /// <summary>The step's JSON definitions, shown collapsed above the code.</summary>
    public IEnumerable<CodeBlock> Definitions(ManifestStep step) => step.Files
        .Where(file => file.Key.StartsWith("definition", StringComparison.Ordinal))
        .Select(file => new CodeBlock("definition", file.Value, "json",
            File.ReadAllText(System.IO.Path.Combine(env.ContentRootPath, file.Value)).Trim(), Blob(file.Value)));

    /// <summary>The regions the step lists, from its client file and every server* file, in manifest order.</summary>
    public IEnumerable<CodeBlock> Regions(ManifestStep step)
    {
        foreach (var (source, path) in step.Files)
        {
            if (source != "client" && !source.StartsWith("server", StringComparison.Ordinal))
            {
                continue;
            }
            var role = source == "client" ? "client" : "server";
            var language = path.EndsWith(".cs", StringComparison.Ordinal) ? "csharp" : "javascript";
            foreach (var region in RegionsIn(env.ContentRootPath, path).Where(r => step.Regions.Contains(r.Id)).DistinctBy(r => r.Id))
            {
                yield return new CodeBlock(role, path, language, region.Code, $"{Blob(path)}#L{region.Start}-L{region.End}", region.Id);
            }
        }
    }

    private string Blob(string path) => $"{options.Value.GitHub}/blob/{manifest.Branch}/{path}";

    private static string Dedent(string[] lines)
    {
        var indent = lines.Where(line => line.Trim().Length > 0).Select(line => line.Length - line.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join("\n", lines.Select(line => line.Length >= indent ? line[indent..] : line.TrimStart()));
    }

    [GeneratedRegex(@"^\s*(?://\s*)?#region\s+sjs:(\S+)")]
    private static partial Regex RegionStart();

    [GeneratedRegex(@"^\s*(?://\s*)?#endregion")]
    private static partial Regex RegionEnd();
}
