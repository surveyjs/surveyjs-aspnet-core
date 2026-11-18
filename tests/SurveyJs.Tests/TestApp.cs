using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Tests;

/// <summary>
/// The app in memory, on a temporary DATA_DIR of its own (migrated and seeded at start), with the settings a test gives.
/// SURVEYJS_TEST_HOST skips the port check: TestServer listens on no port.
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> settings;
    private readonly Action<IServiceCollection>? services;

    public TestApp(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null)
    {
        DataDir = Path.Combine(Path.GetTempPath(), "surveyjs-tests", Guid.NewGuid().ToString("N"));
        this.settings = new Dictionary<string, string?>
        {
            ["SURVEYJS_TEST_HOST"] = "true",
            ["DATA_DIR"] = DataDir,
            ["SURVEYJS_BUILD"] = "test",
            ["SURVEYJS_SERVICE_URL"] = "http://127.0.0.1:9",   // nothing listens: the service tests stub it
            ["VALIDATE_RESPONSES"] = "false",
            ["LINT_DEFINITIONS"] = "false",
            ["DEMO_MODE"] = "false",
            ["AI_API_KEY"] = "",
        };
        foreach (var (key, value) in settings ?? []) this.settings[key] = value;
        this.services = services;
    }

    public string DataDir { get; }

    public string DatabasePath => Path.Combine(DataDir, "database.sqlite");

    /// <summary>What the app logged, for the checks that answers never reach the log.</summary>
    public ConcurrentQueue<string> Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
        builder.ConfigureLogging(logging => logging.AddProvider(new ListLogger(Logs)));
        builder.ConfigureTestServices(collection => services?.Invoke(collection));
    }

    /// <summary>A client with the demo_user cookie set to this user (alice, bob or none).</summary>
    public HttpClient As(string user = "alice", string? sandbox = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", sandbox is null ? $"demo_user={user}" : $"demo_user={user}; demo_sid={sandbox}");
        return client;
    }

    /// <summary>A short-lived context on the main database, to read back what the app stored.</summary>
    public SurveyDb Db()
    {
        _ = Server;   // started, so migrated and seeded
        return SurveyDbSetup.Open(DatabasePath);
    }

    /// <summary>Runs one SQL query on the database file directly and returns the first column of each row as text.</summary>
    public List<string?> Sql(string sql, string? database = null)
    {
        _ = Server;   // started, so migrated and seeded
        using var connection = new SqliteConnection(SurveyDbSetup.ConnectionString(database ?? DatabasePath));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string?>();
        while (reader.Read()) rows.Add(reader.IsDBNull(0) ? null : Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture));
        return rows;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(DataDir, recursive: true);
        }
        catch (IOException)
        {
            // Windows may hold a file a moment longer; the folder is under the temp directory
        }
    }

    private sealed class ListLogger(ConcurrentQueue<string> logs) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            logs.Enqueue($"{logLevel}: {formatter(state, exception)} {exception}");
        public void Dispose() { }
    }
}

/// <summary>Helpers for requests and answers.</summary>
public static class Http
{
    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public static async Task<JsonNode?> JsonAsync(this HttpResponseMessage answer) =>
        JsonNode.Parse(await answer.Content.ReadAsStringAsync(Token));

    /// <summary>The { error } of an answer, after checking its status and that it is JSON.</summary>
    public static async Task<string?> ErrorAsync(this HttpResponseMessage answer, HttpStatusCode status)
    {
        Assert.Equal(status, answer.StatusCode);
        Assert.StartsWith("application/json", answer.Content.Headers.ContentType?.MediaType);
        return (string?)(await answer.JsonAsync())?["error"];
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, string json) => client.PostAsync(url, Json(json), Token);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, string json) => client.PutAsync(url, Json(json), Token);

    public static Task<string> TextAsync(this HttpClient client, string url) => client.GetStringAsync(url, Token);

    public static MultipartFormDataContent Files(params (string Field, string Name, byte[] Bytes)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (field, name, bytes) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, field, name);
        }
        return form;
    }

    public static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    public static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%test\n");
}

/// <summary>Stands in for the SurveyJS service: answers each call with what the test set, and records the requests.</summary>
public sealed class StubService : HttpMessageHandler
{
    public Func<HttpRequestMessage, string, HttpResponseMessage> Answer { get; set; } = (_, _) => Reply(200, "{}");

    public ConcurrentQueue<(string Path, string Body)> Calls { get; } = new();

    public static HttpResponseMessage Reply(int status, string body, string type = "application/json") =>
        new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, type) };

    public static HttpResponseMessage Bytes(int status, byte[] body, string type) =>
        new((HttpStatusCode)status) { Content = new ByteArrayContent(body) { Headers = { ContentType = new(type) } } };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Enqueue((request.RequestUri!.AbsolutePath, body));
        return Answer(request, body);
    }

    /// <summary>The app with this stub as the SurveyJS service, and the switches on.</summary>
    public static (TestApp App, StubService Service) App(Dictionary<string, string?>? settings = null)
    {
        var stub = new StubService();
        var all = new Dictionary<string, string?> { ["VALIDATE_RESPONSES"] = "true", ["LINT_DEFINITIONS"] = "true" };
        foreach (var (key, value) in settings ?? []) all[key] = value;
        var app = new TestApp(all, services => services.AddHttpClient<SurveyJsService>().ConfigurePrimaryHttpMessageHandler(() => stub));
        return (app, stub);
    }
}
