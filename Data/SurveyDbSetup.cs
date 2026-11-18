using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;
using SurveyJsAspNetCore.Configuration;

namespace SurveyJsAspNetCore.Data;

/// <summary>
/// Where this request reads and writes: the database file and the uploads folder. Outside demo mode that is
/// &lt;DATA_DIR&gt;/database.sqlite and &lt;DATA_DIR&gt;/uploads; in demo mode the DemoSandbox middleware points it at the
/// visitor's private copy (Demo/DemoSandbox.cs). SurveyDb and the file endpoints read it, so endpoint code doesn't change.
/// </summary>
public sealed class RequestStorage(IOptions<SurveyJsOptions> options)
{
    public string DatabasePath { get; set; } = options.Value.DatabasePath;
    public string UploadsPath { get; set; } = options.Value.UploadsPath;

    /// <summary>The visitor's sandbox id in demo mode (room keys and the Data panel use it), otherwise null.</summary>
    public string? SandboxId { get; set; }

    public string ConnectionString => SurveyDbSetup.ConnectionString(DatabasePath);
}

/// <summary>The SQLite database through EF Core: registration, migrations on start, and short-lived contexts.</summary>
public static class SurveyDbSetup
{
    private static string sharedRoot = Path.Combine(AppContext.BaseDirectory, "shared");

    public static string ConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    /// <summary>
    /// One SurveyDb per request, on the request's database (RequestStorage). Not AddDbContextPool: a pooled context
    /// can't take a different database per request.
    /// </summary>
    public static IServiceCollection AddSurveyDb(this IServiceCollection services, string contentRoot)
    {
        sharedRoot = Path.Combine(contentRoot, "shared");
        services.AddScoped<RequestStorage>();
        services.AddDbContext<SurveyDb>((provider, options) =>
            Configure(options, provider.GetRequiredService<RequestStorage>().ConnectionString));
        return services;
    }

    /// <summary>
    /// A context of its own on this database file, for code that outlives a request's scope: the relays (a WebSocket
    /// lives for minutes, and a DbContext isn't thread-safe) and the sandboxes. Dispose it after each use.
    /// </summary>
    public static SurveyDb Open(string databasePath)
    {
        var options = new DbContextOptionsBuilder<SurveyDb>();
        Configure(options, ConnectionString(databasePath));
        return new SurveyDb(options.Options);
    }

    /// <summary>Create or update the tables and seed what is missing. Runs on start, before the app takes requests.</summary>
    public static async Task MigrateAsync(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using var db = Open(databasePath);
        await db.Database.MigrateAsync();
    }

    private static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        var seeder = new Seeder(sharedRoot);
        options.UseSqlite(connectionString)
            // Reads need no AsNoTracking(); writes add new entities or go through StoredJson.UpsertAsync
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            // Both, as the EF Core docs ask: MigrateAsync() calls the async one, `dotnet ef database update` the other
            .UseSeeding((db, _) => seeder.Seed((SurveyDb)db))
            .UseAsyncSeeding((db, _, cancellation) => seeder.SeedAsync((SurveyDb)db, cancellation));
    }

    /// <summary>
    /// For `dotnet ef`: without it, the tool runs Program.cs up to builder.Build(), and the port check refuses to start
    /// while the app is running. Uses storage/database.sqlite under the current folder.
    /// </summary>
    public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<SurveyDb>
    {
        public SurveyDb CreateDbContext(string[] args)
        {
            sharedRoot = Path.GetFullPath("shared");
            return Open(Path.GetFullPath(Path.Combine("storage", "database.sqlite")));
        }
    }
}
