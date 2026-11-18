using System.Net;
using System.Text.Json.Nodes;
using static SurveyJsAspNetCore.Tests.Http;

namespace SurveyJsAspNetCore.Tests;

/// <summary>store-files: the batch rules, the 5 MB limit, and downloads that never run as the site.</summary>
public sealed class StoreFilesTests : IAsyncLifetime
{
    private readonly TestApp app = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => app.DisposeAsync();

    private int FileRows() => int.Parse(app.Sql("SELECT COUNT(*) FROM files").Single()!);

    [Fact]
    public async Task SeveralFilesAreStoredInOrderAndServedBack()
    {
        var answer = await app.As().PostAsync("/api/files", Files(("files[]", "a.png", Png), ("files[]", "b.pdf", Pdf)), Token);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        var ids = (await answer.JsonAsync())!.AsArray().Select(id => (string)id!).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal(["a.png", "b.pdf"], ids.Select(id => app.Sql($"SELECT name FROM files WHERE id = '{id}'").Single()));

        var download = await app.As().GetAsync($"/api/files/{ids[1]}", Token);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync(Token));
    }

    [Fact]
    public async Task OneBadFileRejectsTheWholeBatch()
    {
        var before = FileRows();
        var answer = await app.As().PostAsync("/api/files", Files(("files[]", "ok.png", Png), ("files[]", "page.html", "<script>alert(1)</script>"u8.ToArray())), Token);
        Assert.Equal("page.html is not a PNG, JPEG, WebP, GIF or PDF file", await answer.ErrorAsync(HttpStatusCode.UnprocessableEntity));
        Assert.Equal(before, FileRows());

        var none = await app.As().PostAsync("/api/files", Files(("other", "a.png", Png)), Token);
        Assert.NotNull(await none.ErrorAsync(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task AFileOver5MbIsRejected()
    {
        var big = new byte[5 * 1024 * 1024 + 1];
        Png.CopyTo(big, 0);
        Assert.Equal("Each file must be 5 MB or less", await (await app.As().PostAsync("/api/files", Files(("files[]", "big.png", big)), Token)).ErrorAsync(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task AnHtmlFileIsServedWithNosniffAndSandbox()
    {
        // Uploads only accept detected images and PDFs; a file stored by other means still never runs as this site
        Directory.CreateDirectory(Path.Combine(app.DataDir, "uploads"));
        _ = app.Server;
        await File.WriteAllTextAsync(Path.Combine(app.DataDir, "uploads", "html-file"), "<script>alert(document.cookie)</script>", Token);
        app.Sql("INSERT INTO files (id, name, type) VALUES ('html-file', 'page.html', 'text/html')");
        var answer = await app.As().GetAsync("/api/files/html-file", Token);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("nosniff", answer.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("sandbox", answer.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task SignedOutDownloadsAnswer404()
    {
        var id = (string)(await (await app.As().PostAsync("/api/files", Files(("files[]", "a.png", Png)), Token)).JsonAsync())![0]!;
        Assert.Equal("Not Found", await (await app.As("none").GetAsync($"/api/files/{id}", Token)).ErrorAsync(HttpStatusCode.NotFound));
        Assert.Equal("Not Found", await (await app.As().GetAsync("/api/files/unknown", Token)).ErrorAsync(HttpStatusCode.NotFound));
    }
}
