using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SurveyJsAspNetCore.Authorization;
using SurveyJsAspNetCore.Configuration;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Demo;
using SurveyJsAspNetCore.Endpoints;
using SurveyJsAspNetCore.Integration;

var builder = WebApplication.CreateBuilder(args);
var contentRoot = builder.Environment.ContentRootPath;

// Running it locally: which checkout answers, read from git once, shown on the pages as "Running <build>"
if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(builder.Configuration["SURVEYJS_BUILD"]))
{
    builder.Configuration["SURVEYJS_BUILD"] = LocalRun.GitBuild(contentRoot);
}
var settings = SurveyJsOptions.From(builder.Configuration, contentRoot);

// Refuse a busy port before starting: on Windows a second server can start on a port another one holds, which keeps
// answering. APP_HOST and APP_PORT win over ASPNETCORE_URLS and ASPNETCORE_HTTP_PORTS (the image sets APP_HOST=0.0.0.0).
if (!builder.Configuration.GetValue<bool>("SURVEYJS_TEST_HOST"))
{
    if (LocalRun.IsBusy(settings.AppPort))
    {
        Console.Error.WriteLine(LocalRun.BusyMessage(settings.AppPort));
        return 1;
    }
    builder.WebHost.UseUrls($"http://{settings.AppHost}:{settings.AppPort}");
}

// Configuration: every setting, bound once (Configuration/SurveyJsOptions.cs)
builder.Services.AddOptions<SurveyJsOptions>().Configure<IConfiguration>((options, config) => options.Load(config, contentRoot));

// Storage: SQLite through EF Core, the request's database (Data/SurveyDbSetup.cs)
builder.Services.AddSurveyDb(contentRoot);

// Errors under /api are { error } with their status, the framework's own included (Integration/ApiErrors.cs)
builder.Services.AddApiErrors();
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

// Request size: files up to 5 MB each (checked in the endpoints), the extraction's base64 body to the service
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 24 * 1024 * 1024);
builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = 12 * 1024 * 1024);

// Run Server-Side Workflows through the SurveyJS service, and ai-translation through your AI provider: typed HttpClients
// with a timeout per call (Integration/SurveyJsService.cs, AiTranslationClient.cs)
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<SurveyJsService>(http => http.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddHttpClient<AiTranslationClient>(http => http.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddHttpClient("restcountries", http => http.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddMemoryCache();

// Who the user is and what they may do. The demo signs users in from a cookie (Demo/DemoUsers.cs); a real app uses
// cookie or JWT authentication here. The two policies are the integration's permission hooks (Authorization/).
builder.Services.AddAuthentication(DemoUsers.Scheme)
    .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>(DemoUsers.Scheme, null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(EditFormsRequirement.Policy, policy => policy.AddRequirements(new EditFormsRequirement()))
    .AddPolicy(ReadFileRequirement.Policy, policy => policy.AddRequirements(new ReadFileRequirement()));
builder.Services.AddSingleton<IAuthorizationHandler, ReadFileHandler>();

// The catalog and the example pages (Pages/). /claim is Option A of load-definition-variables-data.
builder.Services.AddRazorPages(pages => pages.Conventions.AddPageRoute("/Examples/LoadDefinitionVariablesData", "/claim"));
builder.Services.AddHealthChecks();

// The keys that protect cookies and antiforgery tokens live with the data, so they survive a restart of the container
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(settings.DataDir, "keys")));

// Demo only: the manifest, the Code tab, the import map, the visitor sandboxes and the AI limit (Demo/)
builder.Services.AddSingleton(Manifest.Load(contentRoot));
builder.Services.AddSingleton<CodePanel>();
builder.Services.AddSingleton<ImportMap>();
builder.Services.AddSingleton<Sandboxes>();
builder.Services.AddHostedService<SandboxPruner>();
builder.Services.AddDemoAiLimit(settings.AiCallsPerDay);

// The relays run in the app process: rooms live in memory (Relay/)
builder.Services.AddSingleton<SurveyJsAspNetCore.Relay.FillRooms>();
builder.Services.AddSingleton<SurveyJsAspNetCore.Relay.EditRooms>();

// The SurveyJS NuGet packages' files (/_content/SurveyJS.*/) are static web assets: serve them from the build output
// too, not only in Development (a published app has them in wwwroot/_content/)
builder.WebHost.UseStaticWebAssets();

var app = builder.Build();
var options = app.Services.GetRequiredService<IOptions<SurveyJsOptions>>().Value;

// Create or update the tables and seed them: committed migrations, no SDK needed (Data/Migrations/, Data/Seeder.cs)
await SurveyDbSetup.MigrateAsync(options.DatabasePath);
Directory.CreateDirectory(options.UploadsPath);
app.Services.GetRequiredService<ImportMap>();   // fails here when a SurveyJS package lacks its ES module build

app.UseApiErrors();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// shared/ lives outside wwwroot: serve only its client modules, definitions, samples and page UI
var types = new FileExtensionContentTypeProvider();
types.Mappings[".js"] = "text/javascript; charset=utf-8";
types.Mappings[".mjs"] = "text/javascript; charset=utf-8";
foreach (var folder in new[] { "client", "definitions", "samples", "ui" })
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.Combine(contentRoot, "shared", folder)),
        RequestPath = $"/shared/{folder}",
        ContentTypeProvider = types,
    });
}

app.UseMiddleware<DemoSandbox>();   // Demo only: the visitor's private copy of the data
app.UseAuthentication();
app.UseRateLimiter();               // Demo only: the AI limit
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages();
app.MapHealthChecks("/up");
app.MapDemoStored();

// The examples, one file each in Endpoints/, in the docs' order. The switches swap two handlers.
// Run Forms
if (options.ValidateResponses) app.MapValidateResponse(); else app.MapSaveResponse();   // POST /api/responses
app.MapLoadDefinitionVariablesData();       // GET /api/forms/{id} (and /claim, a Razor page)
app.MapResumeProgress();                    // GET|PUT|DELETE /api/progress/{formId}
app.MapStoreFiles();                        // POST /api/files, GET /api/files/{id}
app.MapGroup("").AddEndpointFilter<DemoCacheHeader>().MapChoicesFromWeb();   // GET /api/offices, GET /api/countries
app.MapAsyncFunctions();                    // GET /api/customers/exists, GET /api/shipping
app.MapRelationalStorage();                 // POST /api/claims
app.MapFillTogether();                      // WS /ws/rooms/{recordId}

// Analyze Results with Dashboard
app.MapDashboard();                         // GET /api/responses

// Edit Forms in Survey Creator (creator-uploads reuses POST /api/files from store-files)
if (options.LintDefinitions) app.MapLintDefinition(); else app.MapCreatorLoadSave();     // PUT /api/forms/{id}
app.MapAiTranslation();                     // POST /api/translate
app.MapVariablePresets();                   // GET|PUT /api/variable-presets/{formId}
app.MapEditTogether();                      // WS /ws/forms/{formId}

// Run Server-Side Workflows (validate-response and lint-definition are switched in above)
app.MapServerPdf();                         // GET /api/claims/{id}/pdf
app.MapExtractFromPaper();                  // GET /api/work-orders/blank.pdf, POST /api/work-orders/extract

// Running it locally: which folder and checkout answers, and where
app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine(
    $"Serving {contentRoot}{(options.Build is null ? "" : $" ({options.Build})")} on http://{options.AppHost}:{options.AppPort}"));
await app.RunAsync();
return 0;

/// <summary>The entry point, for WebApplicationFactory&lt;Program&gt; in the tests.</summary>
public partial class Program;
