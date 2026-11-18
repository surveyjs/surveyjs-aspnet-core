using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SurveyJsAspNetCore.Configuration;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Demo;

namespace SurveyJsAspNetCore.Tests;

/// <summary>EF Core: migrations, seeding, sandbox copies and their removal (on Windows too, where pooled connections lock files).</summary>
public sealed class StorageTests
{
    private static readonly string[] Tables = ["forms", "responses", "progress", "files", "offices", "customers", "shipping_rates", "claims", "variable_presets", "users"];

    [Fact]
    public async Task TheModelHasNoChangesWithoutAMigration()
    {
        await using var app = new TestApp();
        _ = app.Server;
        await using var db = app.Db();
        Assert.False(db.Database.HasPendingModelChanges(), "An entity changed without a migration: dotnet ef migrations add <Name> --output-dir Data/Migrations");
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Http.Token));
    }

    [Fact]
    public async Task AFreshDataDirIsMigratedAndSeeded()
    {
        await using var app = new TestApp();
        _ = app.Server;
        var forms = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "../../../../../shared/seed/forms.json"), Http.Token))
            .Split('"').Where((_, i) => i % 4 == 1).ToList();
        Assert.Equal(forms.Order(), app.Sql("SELECT key FROM forms").Order()!);
        Assert.Equal("2", app.Sql("SELECT COUNT(*) FROM users").Single());
        Assert.True(int.Parse(app.Sql("SELECT COUNT(*) FROM responses WHERE form_id = 'feedback'").Single()!) > 150);
        Assert.Equal("wal", app.Sql("PRAGMA journal_mode").Single());
    }

    [Fact]
    public async Task ASecondStartAddsNothingAndADeletedFormComesBack()
    {
        await using var app = new TestApp();
        _ = app.Server;
        var before = Tables.ToDictionary(t => t, t => app.Sql($"SELECT COUNT(*) FROM {t}").Single());
        app.Sql("DELETE FROM forms WHERE key = 'support'");

        await SurveyDbSetup.MigrateAsync(app.DatabasePath);   // what the next start runs
        var after = Tables.ToDictionary(t => t, t => app.Sql($"SELECT COUNT(*) FROM {t}").Single());
        Assert.Equal(before, after);
        Assert.Single(app.Sql("SELECT key FROM forms WHERE key = 'support'"));
    }

    [Fact]
    public async Task ASandboxCopyHasEverySeededRowAndAPrunedSandboxIsGone()
    {
        await using var app = new TestApp(new() { ["DEMO_MODE"] = "true" });
        var sid = Sandboxes.NewId();
        var answer = await app.As("alice", sid).GetAsync("/api/forms/contact", Http.Token);
        answer.EnsureSuccessStatusCode();

        var sandboxes = app.Services.GetRequiredService<Sandboxes>();
        var copy = sandboxes.DatabasePath(sid);
        Assert.True(File.Exists(copy));
        foreach (var table in Tables)
        {
            Assert.Equal(app.Sql($"SELECT COUNT(*) FROM {table}").Single(), app.Sql($"SELECT COUNT(*) FROM {table}", copy).Single());
        }

        // A write goes to the sandbox only
        await app.As("alice", sid).PostAsync("/api/responses", Http.Json("""{"formId":"contact","data":{"q":1}}"""), Http.Token);
        Assert.NotEqual(app.Sql("SELECT COUNT(*) FROM responses").Single(), app.Sql("SELECT COUNT(*) FROM responses", copy).Single());

        // Unused for longer than the TTL: removed, with its -wal and -shm files and its uploads folder
        var ttl = app.Services.GetRequiredService<IOptions<SurveyJsOptions>>().Value.SandboxTtlHours;
        foreach (var file in new[] { copy, copy + "-wal", copy + "-shm" }.Where(File.Exists))
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-ttl - 1));
        }
        Assert.Equal(1, sandboxes.Prune());
        Assert.False(File.Exists(copy));
        Assert.False(Directory.Exists(sandboxes.UploadsPath(sid)));
    }

    [Fact]
    public async Task JsonValueFiltersInsideADocument()
    {
        await using var app = new TestApp();
        _ = app.Server;
        await using var db = app.Db();
        var bobs = await db.Responses.Where(r => r.FormId == "claim" && SurveyDb.JsonValue(r.Data, "$.customer_email") == "bob@example.com").CountAsync(Http.Token);
        Assert.Equal(1, bobs);
        SqliteConnection.ClearAllPools();
    }
}
