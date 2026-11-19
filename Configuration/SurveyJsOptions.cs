namespace SurveyJsAspNetCore.Configuration;

/// <summary>
/// Every SurveyJS and demo setting, bound once. The keys are the cross-platform names (APP_PORT, SURVEYJS_SERVICE_URL…),
/// so environment variables, appsettings.json, user secrets and the contract runner use the same words. appsettings.json
/// (committed) holds every setting but the private keys; those come from user secrets in Development
/// (<c>dotnet user-secrets set AI_API_KEY …</c>) and from environment variables elsewhere. Code reads
/// <c>IOptions&lt;SurveyJsOptions&gt;</c>, never <c>Environment.GetEnvironmentVariable</c>.
/// </summary>
public sealed class SurveyJsOptions
{
    // Running it locally: where `dotnet run` listens, and which checkout answers (`<branch>@<short commit>`, the pages'
    // "Running …" line). Program.cs reads git once in Development when SURVEYJS_BUILD is unset; hosted demos set it at deploy.
    public int AppPort { get; set; } = 8000;
    public string AppHost { get; set; } = "127.0.0.1";
    public string? Build { get; set; }

    // Shown in the browser: setLicenseKey() runs only when it is set
    public string? LicenseKey { get; set; }

    // Run Server-Side Workflows: SurveyJS Server (github.com/surveyjs/surveyjs-server), run next to the app
    public string ServiceUrl { get; set; } = "http://localhost:3010";
    public bool ValidateResponses { get; set; }
    public bool LintDefinitions { get; set; }

    // ai-translation: an OpenAI-compatible /chat/completions endpoint
    public string AiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string? AiApiKey { get; set; }
    public string AiModel { get; set; } = "gpt-4o-mini";

    // extract-from-paper: the app never reads these itself; Docker Compose passes them to the SurveyJS service
    public string? ExtractionProvider { get; set; }
    public string? ExtractionModel { get; set; }
    public string? ExtractionOllamaBaseUrl { get; set; }
    public string? OpenAiApiKey { get; set; }
    public string? AnthropicApiKey { get; set; }

    // The relays run in the app: empty means the page's own origin (ws or wss by the request's scheme)
    public string? RelayUrl { get; set; }
    public string? EditRelayUrl { get; set; }

    // The database, uploads, visitor sandboxes and relay snapshots (absolute; DATA_DIR defaults to storage/)
    public string DataDir { get; set; } = "storage";

    // Demo only: a private SQLite copy and uploads folder per visitor (README: How data works in the demo)
    public bool DemoMode { get; set; }
    public int SandboxTtlHours { get; set; } = 24;
    public long MaxDemoUploadBytes { get; set; } = 5 * 1024 * 1024;
    public int AiCallsPerDay { get; set; } = 20;

    // The Server Integration docs: step pages link to <Docs>#<anchor> (anchor: surveyjs-integration.json)
    public string Docs { get; set; } = "https://surveyjs.io/documentation/backend-integration-aspnet-core";
    public string GitHub { get; set; } = "https://github.com/surveyjs/surveyjs-aspnet-core";

    /// <summary>Reads every setting from configuration (environment variables win over user secrets, and both over
    /// appsettings.json).</summary>
    public static SurveyJsOptions From(IConfiguration config, string contentRoot)
    {
        var options = new SurveyJsOptions();
        options.Load(config, contentRoot);
        return options;
    }

    public void Load(IConfiguration config, string contentRoot)
    {
        AppPort = int.TryParse(config["APP_PORT"], out var port) ? port : AppPort;
        AppHost = Text(config["APP_HOST"]) ?? AppHost;
        Build = Text(config["SURVEYJS_BUILD"]);
        LicenseKey = Text(config["SURVEYJS_LICENSE_KEY"]);
        ServiceUrl = (Text(config["SURVEYJS_SERVICE_URL"]) ?? ServiceUrl).TrimEnd('/');
        ValidateResponses = Switch(config["VALIDATE_RESPONSES"]);
        LintDefinitions = Switch(config["LINT_DEFINITIONS"]);
        AiBaseUrl = (Text(config["AI_BASE_URL"]) ?? AiBaseUrl).TrimEnd('/');
        AiApiKey = Text(config["AI_API_KEY"]);
        AiModel = Text(config["AI_MODEL"]) ?? AiModel;
        ExtractionProvider = Text(config["SURVEYJS_AI_PROVIDER"]);
        ExtractionModel = Text(config["SURVEYJS_AI_MODEL"]);
        ExtractionOllamaBaseUrl = Text(config["SURVEYJS_OLLAMA_BASE_URL"]);
        OpenAiApiKey = Text(config["OPENAI_API_KEY"]);
        AnthropicApiKey = Text(config["ANTHROPIC_API_KEY"]);
        RelayUrl = Text(config["RELAY_URL"])?.TrimEnd('/');
        EditRelayUrl = Text(config["EDIT_RELAY_URL"])?.TrimEnd('/');
        DataDir = Path.GetFullPath(Text(config["DATA_DIR"]) ?? "storage", contentRoot);
        DemoMode = Switch(config["DEMO_MODE"]);
        Docs = Text(config["SurveyJs:Docs"]) ?? Docs;
        GitHub = (Text(config["SurveyJs:GitHub"]) ?? GitHub).TrimEnd('/');
    }

    /// <summary>The switches (VALIDATE_RESPONSES, LINT_DEFINITIONS, DEMO_MODE) are on for 1, true, on or yes.</summary>
    public static bool Switch(string? value) => value?.Trim().ToLowerInvariant() is "1" or "true" or "on" or "yes";

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public string DatabasePath => Path.Combine(DataDir, "database.sqlite");
    public string UploadsPath => Path.Combine(DataDir, "uploads");
    public string SandboxPath => Path.Combine(DataDir, "sandbox");
    public string RelaySnapshotPath => Path.Combine(DataDir, "relay");
}
