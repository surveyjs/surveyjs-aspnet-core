// dotnet run --project tools/Contract -- [--steps save-response,choices-from-web] [--smoke]
//
// Runs the shared contract test (shared/contract-tests/run.mjs), or the smoke test (shared/smoke/run.mjs), against the
// built app on a free port and a fresh DATA_DIR (storage/contract/run-<time>), then stops the app and exits with the
// test's exit code. It builds the app first (dotnet build at the root) and starts it with `dotnet <built dll>`, not
// `dotnet run`, which starts a second process that is hard to stop on Windows. Works on Windows and Linux; needs
// Node 22+ on PATH for the test itself.
//
// Passed through from the environment: SERVICE (off or on; off points the app at a port nothing listens on), AI (off or
// stub), VALIDATE_RESPONSES, LINT_DEFINITIONS, DEMO_MODE, SURVEYJS_SERVICE_URL and the AI_* settings. With AI=stub,
// start the stub first (docker-compose.ci.yml) and set AI_BASE_URL=http://127.0.0.1:3020/v1 AI_API_KEY=stub
// AI_MODEL=stub. The relays run in the app, so RELAY_URL and EDIT_RELAY_URL are its ws:// origin.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

var root = FindRoot();
var joined = string.Join(' ', args);
var smoke = joined.Contains("--smoke");
var steps = Regex.Match(joined, @"--steps[= ](\S+)");

// Build the app (a no-op when it is up to date)
if (Run("dotnet", ["build", Path.Combine(root, "surveyjs-aspnet-core.csproj"), "--nologo", "-v", "q"], root, null) != 0)
{
    Console.Error.WriteLine("dotnet build failed");
    return 1;
}
var dll = Path.Combine(root, "bin", "Debug", "net10.0", "surveyjs-aspnet-core.dll");

var dataDir = Path.Combine(root, "storage", "contract", $"run-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
Directory.CreateDirectory(dataDir);
var port = FreePort();
var origin = $"http://127.0.0.1:{port}";

var env = new Dictionary<string, string?>
{
    ["ASPNETCORE_ENVIRONMENT"] = "Production",   // appsettings.Development.json and user secrets must not switch anything on
    ["DATA_DIR"] = dataDir,
    ["APP_PORT"] = port.ToString(),
    ["APP_HOST"] = "127.0.0.1",
    ["SERVICE"] = Env("SERVICE", "off"),
    ["AI"] = Env("AI", "off"),
    ["SURVEYJS_BUILD"] = Env("SURVEYJS_BUILD", "contract"),
};
foreach (var name in new[] { "VALIDATE_RESPONSES", "LINT_DEFINITIONS", "DEMO_MODE" })
{
    env[name] = Env(name, "false");   // what the test expects is what the app runs with
}
if (env["AI"] == "stub")
{
    foreach (var name in new[] { "AI_BASE_URL", "AI_API_KEY", "AI_MODEL" }) env[name] = Environment.GetEnvironmentVariable(name);
}
else
{
    foreach (var name in new[] { "AI_API_KEY", "SURVEYJS_AI_PROVIDER", "SURVEYJS_AI_MODEL", "SURVEYJS_OLLAMA_BASE_URL", "OPENAI_API_KEY", "ANTHROPIC_API_KEY" })
        env[name] = "";   // off means off, whatever the developer's environment says
}
env["SURVEYJS_SERVICE_URL"] = env["SERVICE"] == "off"
    ? $"http://127.0.0.1:{FreePort()}"   // off means not running, also when a service answers on 3010
    : Env("SURVEYJS_SERVICE_URL", "http://localhost:3010");

var log = dataDir + ".log";
var app = Start("dotnet", [dll], root, env, log);
var code = 1;
try
{
    await WaitForAsync($"{origin}/up", app);
    Console.WriteLine($"Started the app on port {port} (DATA_DIR {dataDir})");
    var test = new List<string> { Path.Combine("shared", smoke ? "smoke" : "contract-tests", "run.mjs") };
    if (steps.Success) test.Add($"--steps={steps.Groups[1].Value}");
    var testEnv = new Dictionary<string, string?>(env)
    {
        ["BASE_URL"] = origin,
        ["RELAY_URL"] = $"ws://127.0.0.1:{port}",
        ["EDIT_RELAY_URL"] = $"ws://127.0.0.1:{port}",
    };
    code = Run("node", test, root, testEnv);
}
finally
{
    if (!app.HasExited)
    {
        app.Kill(entireProcessTree: true);
        await app.WaitForExitAsync();
    }
    app.Dispose();
    if (code != 0)
    {
        var output = File.Exists(log) ? await File.ReadAllTextAsync(log) : "";
        Console.WriteLine("--- the app's output\n" + output[Math.Max(0, output.Length - 6000)..]);
    }
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            if (Directory.Exists(dataDir)) Directory.Delete(dataDir, recursive: true);
            File.Delete(log);
            break;
        }
        catch (IOException e)   // Windows may hold the stopped app's files a moment longer: storage/contract is disposable
        {
            if (attempt < 5)
            {
                await Task.Delay(500);
                continue;
            }
            Console.WriteLine($"Left {dataDir} behind: {e.Message}");
            break;
        }
    }
}
Console.WriteLine($"Test exit code: {code}");
return code;

string Env(string name, string fallback) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value) ? value : fallback;

static Process Start(string file, IEnumerable<string> arguments, string folder, Dictionary<string, string?> env, string log)
{
    var info = new ProcessStartInfo(file) { WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    foreach (var (name, value) in env) info.Environment[name] = value;
    var process = Process.Start(info) ?? throw new InvalidOperationException($"Couldn't start {file}");
    var writer = new StreamWriter(log) { AutoFlush = true };
    process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (writer) writer.WriteLine(e.Data); };
    process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (writer) writer.WriteLine(e.Data); };
    process.Exited += (_, _) => { lock (writer) writer.Dispose(); };
    process.EnableRaisingEvents = true;
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    return process;
}

static int Run(string file, IEnumerable<string> arguments, string folder, Dictionary<string, string?>? env)
{
    var info = new ProcessStartInfo(file) { WorkingDirectory = folder };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    if (env is not null) foreach (var (name, value) in env) info.Environment[name] = value;
    try
    {
        using var process = Process.Start(info)!;
        process.WaitForExit();
        return process.ExitCode;
    }
    catch (System.ComponentModel.Win32Exception)
    {
        Console.Error.WriteLine($"{file} is not on PATH (the contract test needs Node.js 22 or newer)");
        return 1;
    }
}

static async Task WaitForAsync(string url, Process app)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
    var deadline = DateTime.UtcNow.AddSeconds(60);
    while (DateTime.UtcNow < deadline)
    {
        if (app.HasExited) throw new InvalidOperationException($"The app stopped with exit code {app.ExitCode}");
        try
        {
            if ((await http.GetAsync(url)).StatusCode == HttpStatusCode.OK) return;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            await Task.Delay(200);
        }
    }
    throw new InvalidOperationException($"The app did not answer {url} within 60 s");
}

static int FreePort()
{
    using var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    return ((IPEndPoint)probe.LocalEndpoint).Port;
}

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "surveyjs-integration.json"))) return dir.FullName;
    }
    throw new InvalidOperationException("Run this from the repository (no surveyjs-integration.json found)");
}
