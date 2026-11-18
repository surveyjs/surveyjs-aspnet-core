using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SurveyJsAspNetCore.Configuration;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Demo;

/// <summary>
/// Demo only: a private copy of the seeded SQLite database and an uploads folder per visitor (DEMO_MODE=true), in
/// &lt;DATA_DIR&gt;/sandbox/&lt;id&gt;.sqlite and &lt;DATA_DIR&gt;/sandbox/&lt;id&gt;/. Used by the DemoSandbox middleware, the relays
/// (through RequestStorage) and SandboxPruner. Example code never sees it.
/// </summary>
public sealed partial class Sandboxes(IOptions<SurveyJsOptions> options, ILogger<Sandboxes> log)
{
    private readonly ConcurrentDictionary<string, Lazy<Task>> opened = new();

    /// <summary>128 random bits as 32 lowercase hex characters; nothing else ever reaches a file path.</summary>
    public static bool IsValidId(string? id) => id is not null && SandboxId().IsMatch(id);

    public static string NewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public string DatabasePath(string id) => Path.Combine(options.Value.SandboxPath, id + ".sqlite");

    public string UploadsPath(string id) => Path.Combine(options.Value.SandboxPath, id);

    public bool Exists(string? id) => IsValidId(id) && File.Exists(DatabasePath(id!));

    /// <summary>
    /// Point the request's storage at the visitor's copy, creating it from the seeded database on first use.
    /// The middleware calls it for HTTP requests and WebSocket handshakes alike.
    /// </summary>
    public async Task ActivateAsync(string id, RequestStorage storage)
    {
        if (!IsValidId(id))
        {
            throw new ArgumentException("Invalid sandbox id", nameof(id));
        }
        var open = opened.GetOrAdd(id, key => new Lazy<Task>(() => OpenAsync(key)));
        try
        {
            await open.Value;
        }
        catch
        {
            opened.TryRemove(new KeyValuePair<string, Lazy<Task>>(id, open));   // let the next request try again
            throw;
        }
        Touch(DatabasePath(id));
        storage.DatabasePath = DatabasePath(id);
        storage.UploadsPath = UploadsPath(id);
        storage.SandboxId = id;
    }

    /// <summary>
    /// EF Core creates the database in WAL mode, so a File.Copy could miss what is still in the -wal file:
    /// BackupDatabase copies it whole. The first time a process opens a sandbox, it migrates it, so a sandbox left over
    /// from before an upgrade gets the new tables.
    /// </summary>
    private async Task OpenAsync(string id)
    {
        var database = DatabasePath(id);
        Directory.CreateDirectory(UploadsPath(id));
        if (!File.Exists(database))
        {
            var copy = database + ".tmp";
            using (var source = new SqliteConnection(SurveyDbSetup.ConnectionString(options.Value.DatabasePath)))
            using (var target = new SqliteConnection(SurveyDbSetup.ConnectionString(copy) + ";Pooling=False"))
            {
                source.Open();
                target.Open();
                source.BackupDatabase(target);
            }
            File.Move(copy, database, overwrite: true);
        }
        await SurveyDbSetup.MigrateAsync(database);
    }

    /// <summary>Last used: SandboxPruner removes sandboxes unused for 24 hours. At most once a minute.</summary>
    private static void Touch(string database)
    {
        try
        {
            if (File.GetLastWriteTimeUtc(database) < DateTime.UtcNow.AddMinutes(-1))
            {
                File.SetLastWriteTimeUtc(database, DateTime.UtcNow);
            }
        }
        catch (IOException)
        {
            // another request is touching it: either one counts
        }
    }

    /// <summary>Remove sandboxes (and relay snapshots) unused for SandboxTtlHours. Returns how many were removed.</summary>
    public int Prune()
    {
        var cutoff = DateTime.UtcNow.AddHours(-options.Value.SandboxTtlHours);
        var removed = 0;
        if (Directory.Exists(options.Value.SandboxPath))
        {
            foreach (var database in Directory.GetFiles(options.Value.SandboxPath, "*.sqlite"))
            {
                var id = Path.GetFileNameWithoutExtension(database);
                if (!IsValidId(id) || LastUsed(database) >= cutoff)
                {
                    continue;
                }
                try
                {
                    // Microsoft.Data.Sqlite pools connections and keeps the file open: on Windows, File.Delete would fail
                    using (var connection = new SqliteConnection(SurveyDbSetup.ConnectionString(database)))
                    {
                        SqliteConnection.ClearPool(connection);
                    }
                    opened.TryRemove(id, out _);
                    foreach (var file in new[] { database, database + "-wal", database + "-shm" })
                    {
                        File.Delete(file);
                    }
                    if (Directory.Exists(UploadsPath(id)))
                    {
                        Directory.Delete(UploadsPath(id), recursive: true);
                    }
                    removed++;
                }
                catch (IOException e)
                {
                    log.LogWarning(e, "Couldn't remove sandbox {Id}; the next run tries again", id);
                }
            }
        }
        if (Directory.Exists(options.Value.RelaySnapshotPath))
        {
            foreach (var snapshot in Directory.GetFiles(options.Value.RelaySnapshotPath, "*.json").Where(f => File.GetLastWriteTimeUtc(f) < cutoff))
            {
                File.Delete(snapshot);
            }
        }
        return removed;
    }

    private static DateTime LastUsed(string database)
    {
        var wal = database + "-wal";
        var last = File.GetLastWriteTimeUtc(database);
        return File.Exists(wal) && File.GetLastWriteTimeUtc(wal) > last ? File.GetLastWriteTimeUtc(wal) : last;
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex SandboxId();
}

/// <summary>
/// Demo only: with DEMO_MODE=true each visitor works in a private copy of the seeded database and uploads folder, chosen
/// by the demo_sid cookie. Example code doesn't change: SurveyDb and the file endpoints read RequestStorage.
/// Delete this file (and its line in Program.cs) when you copy an example into your app.
/// </summary>
public sealed class DemoSandbox(RequestDelegate next, Sandboxes sandboxes, IOptions<SurveyJsOptions> options)
{
    public const string Cookie = "demo_sid";
    public const string JoinFailed = "demo_join_failed";

    /// <summary>Static files need no sandbox, and must not each create one before the page's cookie arrives.</summary>
    private static readonly string[] Skipped = ["/_content", "/css", "/shared", "/up", "/favicon.ico", "/robots.txt"];

    public async Task InvokeAsync(HttpContext context, RequestStorage storage)
    {
        if (!options.Value.DemoMode || Skipped.Any(path => context.Request.Path.StartsWithSegments(path)))
        {
            await next(context);
            return;
        }

        var id = context.Request.Cookies[Cookie];
        var issueCookie = false;

        // An invite link (?join=<id>) adopts the inviter's sandbox, after the same format check
        var join = context.Request.Query["join"].FirstOrDefault();
        if (join is not null && !ApiErrors.IsApi(context))
        {
            if (sandboxes.Exists(join))
            {
                issueCookie = id != join;
                id = join;
            }
            else
            {
                context.Items[JoinFailed] = true;
                id = null;
            }
        }
        if (!Sandboxes.IsValidId(id))
        {
            id = Sandboxes.NewId();   // never a file path built from client input
            issueCookie = true;
        }
        await sandboxes.ActivateAsync(id!, storage);

        // Uploads are capped at 5 MB per file on the demo
        if (context.Request.HasFormContentType && await TooLargeUploadAsync(context))
        {
            await ApiErrors.WriteAsync(context, StatusCodes.Status413PayloadTooLarge, "Files over 5 MB are not accepted on the demo");
            return;
        }

        if (issueCookie)
        {
            context.Response.Cookies.Append(Cookie, id!, new CookieOptions
            {
                Path = "/", HttpOnly = false, SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps, MaxAge = TimeSpan.FromDays(30),
            });
        }
        await next(context);
    }

    private async Task<bool> TooLargeUploadAsync(HttpContext context)
    {
        try
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);   // the endpoint reads the same parsed form
            return form.Files.Any(file => file.Length > options.Value.MaxDemoUploadBytes);
        }
        catch (InvalidDataException)
        {
            return true;   // over the multipart limit (12 MB)
        }
    }
}

/// <summary>Demo only: removes visitor sandboxes nobody has used for 24 hours, every hour, in demo mode.</summary>
public sealed class SandboxPruner(Sandboxes sandboxes, IOptions<SurveyJsOptions> options, ILogger<SandboxPruner> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.DemoMode)
        {
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            var removed = sandboxes.Prune();
            if (removed > 0)
            {
                log.LogInformation("Removed {Count} sandbox(es) unused for {Hours} hours", removed, options.Value.SandboxTtlHours);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
