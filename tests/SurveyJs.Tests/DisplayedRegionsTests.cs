using SurveyJsAspNetCore.Demo;

namespace SurveyJsAspNetCore.Tests;

/// <summary>The docs show the regions, so the comments on data-corrupting pitfalls must be inside them.</summary>
public sealed class DisplayedRegionsTests
{
    private static string Region(string path, string id) =>
        CodePanel.RegionsIn(ExamplePagesTests.Root, path).Single(region => region.Id == id).Code;

    [Theory]
    [InlineData("Endpoints/SaveResponse.cs", "save-response.server")]
    [InlineData("Endpoints/ResumeProgress.cs", "resume-progress.server")]
    [InlineData("Endpoints/RelationalStorage.cs", "relational-storage.server")]
    [InlineData("Endpoints/CreatorLoadSave.cs", "creator-load-save.server")]
    [InlineData("Endpoints/VariablePresets.cs", "variable-presets.server")]
    [InlineData("Endpoints/ValidateResponse.cs", "validate-response.server")]
    [InlineData("Endpoints/LintDefinition.cs", "lint-definition.server")]
    public void StoringRegionsSayWhyTheyReadTheRawBody(string path, string id)
    {
        var code = Region(path, id);
        Assert.Contains("StoredJson.ReadBodyAsync(request)", code);
        Assert.Matches("(?i)raw (request )?body", code);
    }

    [Fact]
    public void ThePitfallCommentsAreInTheRegions()
    {
        Assert.Contains("1.0", Region("Endpoints/SaveResponse.cs", "save-response.server"));
        Assert.Contains("{} as {}", Region("Endpoints/ValidateResponse.cs", "validate-response.server"));
        Assert.Contains("\"files[]\"", Region("Endpoints/StoreFiles.cs", "store-files.server"));
        Assert.Contains("the detected type, never the client's", Region("Endpoints/StoreFiles.cs", "store-files.server"));
        Assert.Contains("404, not 403", Region("Endpoints/StoreFiles.cs", "store-files.download"));
        Assert.Contains("nosniff and sandbox", Region("Endpoints/StoreFiles.cs", "store-files.download"));
        Assert.Contains("fail closed", Region("Endpoints/ValidateResponse.cs", "validate-response.server"));
        Assert.Contains("fail closed", Region("Endpoints/LintDefinition.cs", "lint-definition.server"));
        Assert.Contains("JSON with base64, not multipart", Region("Endpoints/ExtractFromPaper.cs", "extract-from-paper.server"));
        Assert.Contains("documents as stored, not re-encoded", Region("Endpoints/Dashboard.cs", "dashboard.server"));
        Assert.Contains("@Json.Serialize()", Region("Pages/Examples/LoadDefinitionVariablesData.cshtml.cs", "load-definition-variables-data.embed"));
        Assert.Contains("DisableAntiforgery", Region("Endpoints/StoreFiles.cs", "store-files.server"));
    }
}
