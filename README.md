# SurveyJS + ASP.NET Core: the backend integration examples

This is the .NET code behind [Integrate SurveyJS with ASP.NET Core](https://surveyjs.io/documentation/backend-integration-aspnet-core), as an ASP.NET Core application you can run example by example: minimal API endpoints, Razor Pages and SQLite through Entity Framework Core. Each example of that page has a page here that runs the real SurveyJS component against these endpoints, with a short "Try this" list, a live request log, a "What the server stored" panel and the code that just ran. SurveyJS has no backend of its own, so treat these as starting points for your own app.

- The examples running online: [surveyjs-aspnet-mvc.demos.surveyjs.io](https://surveyjs-aspnet-mvc.demos.surveyjs.io)
- SurveyJS demos: [app.demos.surveyjs.io](https://app.demos.surveyjs.io)
- Backend integration examples on the SurveyJS website: [surveyjs.io/backend-integration/examples](https://surveyjs.io/backend-integration/examples)
- The same examples for other platforms: [PHP (Laravel)](https://github.com/surveyjs/surveyjs-php-laravel), [Node.js (Express)](https://github.com/surveyjs/surveyjs-nodejs-express), [Python (FastAPI)](https://github.com/surveyjs/surveyjs-python-fastapi), [Next.js](https://github.com/surveyjs/surveyjs-nextjs), [Java (Spring Boot)](https://github.com/surveyjs/surveyjs-java-spring-boot)

## Run it

You need the .NET 10 SDK. Docker runs the SurveyJS service for the four Run Server-Side Workflows examples, and Node.js 22 or later is only for the browser tests, the CSS build and the contract test (see [Prerequisites](#prerequisites)).

```bash
git clone https://github.com/surveyjs/surveyjs-aspnet-core.git
cd surveyjs-aspnet-core
dotnet run                             # http://127.0.0.1:8000
docker compose up -d surveyjs          # the SurveyJS service, for Run Server-Side Workflows
```

The first start creates the SQLite database in `storage/` from the committed migrations and seeds it. `dotnet run` uses the `http` profile in `Properties/launchSettings.json` (the `Development` environment, no HTTPS certificate needed).

- **Fill Forms Together** and **Edit Forms Together** use WebSocket relays that run inside the app, on its port (`/ws/rooms/<id>` and `/ws/forms/<id>`). There is nothing else to start.
- **Run Server-Side Workflows** (validate, lint, PDF, extract) call the SurveyJS service, [SurveyJS Server](https://github.com/surveyjs/surveyjs-server). `docker compose up -d surveyjs` starts it on `http://localhost:3010`. To switch Validate Responses and Lint JSON Schemas on, set `VALIDATE_RESPONSES` and `LINT_DEFINITIONS` to `true` (in `appsettings.json`, or as environment variables), then restart the app. Extract Responses from Documents also needs an AI provider for the service: its key in `.env` (copied from `.env.example`), which Compose reads.
- The tests:
  - `dotnet test tests/SurveyJs.Tests`: the xUnit tests, the app in memory with a temporary database per test class, no browser and no network (the SurveyJS service is a stub).
  - `dotnet run --project tools/Contract`: the shared contract test against the built app and a fresh database. Add `-- --smoke` for the smoke test, or `-- --steps save-response,dashboard` for some examples. With the service running, set `SERVICE=on` (`$env:SERVICE="on"` in PowerShell).
  - `npm ci`, `npx playwright install chromium` once, then `dotnet build` and `npm run test:browser`: the shared Playwright specs.
  - `dotnet run --project tools/Check`: the manifest, the regions, `shared/` and the pins.

Before it starts, the app checks that nothing answers on its port. Once it is up, it prints the folder it serves and its `<branch>@<commit>`:

```text
Serving C:\src\surveyjs-aspnet-core (master@4a96b83) on http://127.0.0.1:8000
```

### Prerequisites

- **The .NET 10 SDK** (10.0.400 or a later feature band; `global.json` pins it).
  - Without it, `dotnet run` stops with .NET's own message: "A compatible .NET SDK was not found. Requested SDK version: 10.0.400", followed by the SDKs you have. Install the .NET 10 SDK from [dot.net](https://dotnet.microsoft.com/download/dotnet/10.0).
  - `dotnet --list-sdks` shows what you have.
- **Docker** for the SurveyJS service (`docker compose up -d surveyjs`). Without it, the four Run Server-Side Workflows examples answer `503` "SurveyJS service is not running at http://localhost:3010", and everything else works.
- **Node.js 22 or later**, only for the browser tests, the CSS build and the contract and smoke tests (they are `shared/*.mjs`). `npm ci` on an older Node stops with npm's own `EBADENGINE` error (`engines` in `package.json`, `engine-strict` in `.npmrc`). Running the app never needs Node: the built stylesheet is committed.

On Windows:

- Install the SDK with `winget install Microsoft.DotNet.SDK.10`, or from [dot.net](https://dotnet.microsoft.com/download/dotnet/10.0).
- After installing the SDK or Node, restart your editor, not only its terminal: VS Code's terminals inherit VS Code's environment, so a terminal in an editor started before the install doesn't see the new `PATH`. To refresh the current PowerShell terminal instead:

  ```powershell
  $env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
  dotnet --list-sdks
  ```

- The app listens on `127.0.0.1` by default (`APP_HOST`), so Windows Defender Firewall doesn't ask whether it may accept network connections.
- Set a variable for one run like this, or put it in `appsettings.json` (a private key: `dotnet user-secrets set <name> <value>`):

  ```powershell
  $env:APP_PORT=8001; dotnet run
  ```

  ```bash
  APP_PORT=8001 dotnet run
  ```

  `APP_HOST` and `APP_PORT` decide where the app listens, also when `ASPNETCORE_URLS` is set.

On Windows a second server can start on a port another one holds, and the first one keeps answering. So the app connects to its port before it starts, and stops with exit code 1 when anything answers:

```text
Port 8000 is already in use. Another copy of this app (another checkout or branch?) may be running. Stop it, or set APP_PORT.
```

### Stop everything

`Ctrl+C` in the terminal, then `docker compose down` for the service. To find a server left running in another terminal or checkout:

```powershell
Get-NetTCPConnection -LocalPort 8000 -State Listen | Select-Object OwningProcess
Stop-Process -Id <OwningProcess>
```

```bash
lsof -i :8000
kill <PID>
```

### Run a branch or a PR

- Check the branch out in its own folder, with `git worktree add ../surveyjs-aspnet-core-pr <branch>` (for a pull request, run `git fetch origin pull/<number>/head:pr-<number>` first) or a second clone. Each checkout has its own `storage/` and `bin/`; the user secrets are shared, because they belong to the project's `UserSecretsId`.
- Run one checkout on the default port at a time: stop the other one first, or start the second with `APP_PORT=8001`.
- The SurveyJS service is shared: `docker-compose.yml` names its project `surveyjs-aspnet`, so every checkout manages the same container.
- The page says which checkout answered: "Running `<branch>@<commit>`" under the catalog's footer buttons and at the end of every example's Description panel.

## Docker

```bash
docker compose up --build    # http://127.0.0.1:8000, with the SurveyJS service next to it
```

**The image (`Dockerfile`).**
- **Build and run.** It publishes the app with `mcr.microsoft.com/dotnet/sdk:10.0` and runs it on `mcr.microsoft.com/dotnet/aspnet:10.0`, as the image's non-root `app` user, with `dotnet surveyjs-aspnet-core.dll` as PID 1.
- **What it holds.** The published output includes `shared/`, `surveyjs-integration.json`, the example sources the Code tab shows, and the SurveyJS packages' files under `wwwroot/_content/`.
- **Settings.** Its environment sets `ASPNETCORE_ENVIRONMENT=Production`, `APP_HOST=0.0.0.0`, `DATA_DIR=/app/storage` and `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. Real environment variables win over `appsettings.json`, which the image includes; user secrets aren't read in Production. Compose passes the private keys in from `.env`.
- **Data.** On its first start it migrates and seeds the database, from the compiled migrations: no SDK, no `dotnet ef`. Its one volume, `/app/storage`, holds the database, the uploads, the visitor sandboxes and the relay snapshots.
- **Health.** `GET /up` answers `200 Healthy` without touching the database.

**One process, many visitors.** Kestrel serves requests concurrently, so an extraction that waits up to 120 seconds for the service doesn't block anyone else. Run one process per data volume: the relay rooms, the countries cache and the demo's AI counter live in its memory. Scaling out to several processes needs a shared room store (for example SignalR with a Redis backplane, or Redis pub/sub) and a server database; both are out of scope here.

On Windows, Docker Compose 5 can't build a service from a git URL (`failed to evaluate path "https://github.com/…"`), and the `surveyjs` service is one. It hands the URL to the builder as a file path. Let Compose print the build and give it to the builder yourself, then start without building:

```powershell
docker compose build --print | docker buildx bake -f -
docker compose up -d --no-build
```

### Behind a proxy

`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` makes ASP.NET Core trust `X-Forwarded-For` and `X-Forwarded-Proto` from any proxy. Behind a TLS proxy, the pages' absolute URLs and the relays' `wss://` URLs then use the scheme the visitor sees, and the demo's `Secure` cookies work.

In your own production, narrow it to your proxy: configure `ForwardedHeadersOptions.KnownProxies` (or `KnownNetworks`) with `UseForwardedHeaders()` instead of the variable, and don't publish the app's port.

The relays are on the app's own host and port, so the proxy needs no extra route. It only has to pass WebSocket upgrades through on `/ws/`. For Traefik, next to your entry point and TLS labels:

```yaml
services:
  app:
    labels:
      - traefik.http.routers.surveyjs.rule=Host(`example.com`)
      - traefik.http.services.surveyjs.loadbalancer.server.port=8000
```

- **Traefik** passes WebSocket upgrades on by default.
- **nginx** needs `proxy_set_header Upgrade $http_upgrade;` and `proxy_set_header Connection "upgrade";` on `/ws/`.
- **Azure App Service:** switch **Web sockets** on in the app's configuration.

`RELAY_URL` and `EDIT_RELAY_URL` are only needed when `/ws/` is served from another origin. The SurveyJS service stays internal (`expose: ["3000"]`): only the app calls it.

## Configuration

`Configuration/SurveyJsOptions.cs` binds every setting once. Code reads `IOptions<SurveyJsOptions>`, never the environment. The keys are the cross-platform names, so environment variables, `appsettings.json`, user secrets and the contract runner use the same words as the other platforms. `appsettings.json` is committed and holds every setting except the private keys: change a setting there. The private keys, `SURVEYJS_LICENSE_KEY`, `AI_API_KEY`, `OPENAI_API_KEY` and `ANTHROPIC_API_KEY`, never go in a file in the repository. For `dotnet run` (the Development environment), keep them in user secrets: `dotnet user-secrets set AI_API_KEY <key>` stores them in your profile (`%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`, `~/.microsoft/usersecrets/<UserSecretsId>/secrets.json` on Linux and macOS). Elsewhere, set them as environment variables: for `docker compose`, in a `.env` file next to `docker-compose.yml` (see `.env.example`). Environment variables win over user secrets, and both over `appsettings.json`; a git-ignored `appsettings.Development.json` still works for local overrides. Switches accept `1`, `true`, `on` or `yes`; a blank value means unset.

| Setting | Default | What it does |
|---|---|---|
| `APP_PORT`, `APP_HOST` | `8000`, `127.0.0.1` | Where the app listens. |
| `DATA_DIR` | `storage` | The SQLite database, uploads, sandboxes and relay snapshots. |
| `SURVEYJS_SERVICE_URL` | `http://localhost:3010` | The SurveyJS service (`docker compose up -d surveyjs`). In Compose, the app uses `http://surveyjs:3000`. |
| `VALIDATE_RESPONSES` | `false` | Validate Responses: `MapValidateResponse()` replaces `MapSaveResponse()`. |
| `LINT_DEFINITIONS` | `false` | Lint JSON Schemas: `MapLintDefinition()` replaces the `PUT` of `MapCreatorLoadSave()`. |
| `SURVEYJS_AI_PROVIDER`, `SURVEYJS_AI_MODEL`, `SURVEYJS_OLLAMA_BASE_URL`, `OPENAI_API_KEY`, `ANTHROPIC_API_KEY` | empty | Extract Responses from Documents: Compose passes them to the service as `AI_PROVIDER`, `AI_MODEL` and `OLLAMA_BASE_URL` (`openai`, `anthropic` or `ollama`). Docker Compose reads them from your shell or a `.env` file, not from `appsettings.*.json`. The app reads them only for the page's banner. |
| `AI_BASE_URL`, `AI_API_KEY`, `AI_MODEL` | OpenAI, empty, `gpt-4o-mini` | Translate with AI: any OpenAI-compatible `/chat/completions` endpoint. Without a key, `/api/translate` answers `501`. |
| `SURVEYJS_LICENSE_KEY` | empty | Calls `setLicenseKey()` on every page. Without it, the commercial components show a licence banner. The service at its pinned revision ignores it, so server PDFs carry the SurveyJS alert banner. |
| `RELAY_URL`, `EDIT_RELAY_URL` | empty | The relays' origin when a proxy serves `/ws/` elsewhere, for example `wss://example.com`. Empty: the page's own origin. |
| `DEMO_MODE` | `false` | The hosted demo's visitor sandboxes (below). |
| `SURVEYJS_BUILD` | from git | The `<branch>@<commit>` the pages show as "Running …". Read from git in Development; deploys set it themselves. |
| `SurveyJs:Docs`, `SurveyJs:GitHub` | the ASP.NET Core docs page, this repository | Where the pages link (`SurveyJs__Docs` as an environment variable). |

## The examples

<!-- steps:start (generated by `dotnet run --project tools/Readme` from surveyjs-integration.json) -->
| Example | Run | Server | Client | Page | Licence |
|---|---|---|---|---|---|
| [Save Responses](https://surveyjs.io/documentation/backend-integration-aspnet-core#save-responses) | [/examples/save-response](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/save-response) | [`Endpoints/SaveResponse.cs`](Endpoints/SaveResponse.cs) | [`shared/client/save-response.js`](shared/client/save-response.js) | [`Pages/Examples/SaveResponse.cshtml`](Pages/Examples/SaveResponse.cshtml) | MIT |
| [Load JSON Schemas, Variables, and Answers](https://surveyjs.io/documentation/backend-integration-aspnet-core#load-json-schemas-variables-and-answers) | [/examples/load-definition-variables-data](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/load-definition-variables-data) | [`Endpoints/LoadDefinitionVariablesData.cs`](Endpoints/LoadDefinitionVariablesData.cs) | [`shared/client/load-definition-variables-data.js`](shared/client/load-definition-variables-data.js) | [`Pages/Examples/LoadDefinitionVariablesData.cshtml`](Pages/Examples/LoadDefinitionVariablesData.cshtml) | MIT |
| [Resume Progress](https://surveyjs.io/documentation/backend-integration-aspnet-core#resume-progress) | [/examples/resume-progress](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/resume-progress) | [`Endpoints/ResumeProgress.cs`](Endpoints/ResumeProgress.cs) | [`shared/client/resume-progress.js`](shared/client/resume-progress.js) | [`Pages/Examples/ResumeProgress.cshtml`](Pages/Examples/ResumeProgress.cshtml) | MIT |
| [Store Files Separately](https://surveyjs.io/documentation/backend-integration-aspnet-core#store-files-separately) | [/examples/store-files](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/store-files) | [`Endpoints/StoreFiles.cs`](Endpoints/StoreFiles.cs) | [`shared/client/store-files.js`](shared/client/store-files.js) | [`Pages/Examples/StoreFiles.cshtml`](Pages/Examples/StoreFiles.cshtml) | MIT |
| [Load Choices from Web](https://surveyjs.io/documentation/backend-integration-aspnet-core#load-choices-from-web) | [/examples/choices-from-web](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/choices-from-web) | [`Endpoints/ChoicesFromWeb.cs`](Endpoints/ChoicesFromWeb.cs) | [`shared/client/choices-from-web.js`](shared/client/choices-from-web.js) | [`Pages/Examples/ChoicesFromWeb.cshtml`](Pages/Examples/ChoicesFromWeb.cshtml) | MIT |
| [Async Calculations and Validation](https://surveyjs.io/documentation/backend-integration-aspnet-core#async-calculations-and-validation) | [/examples/async-functions](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/async-functions) | [`Endpoints/AsyncFunctions.cs`](Endpoints/AsyncFunctions.cs) | [`shared/client/async-functions.js`](shared/client/async-functions.js) | [`Pages/Examples/AsyncFunctions.cshtml`](Pages/Examples/AsyncFunctions.cshtml) | MIT |
| [Use Relational Storage](https://surveyjs.io/documentation/backend-integration-aspnet-core#use-relational-storage) | [/examples/relational-storage](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/relational-storage) | [`Endpoints/RelationalStorage.cs`](Endpoints/RelationalStorage.cs) | [`shared/client/relational-storage.js`](shared/client/relational-storage.js) | [`Pages/Examples/RelationalStorage.cshtml`](Pages/Examples/RelationalStorage.cshtml) | MIT |
| [Fill Forms Together](https://surveyjs.io/documentation/backend-integration-aspnet-core#fill-forms-together) | [/examples/fill-together](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/fill-together) | [`Endpoints/FillTogether.cs`](Endpoints/FillTogether.cs) | [`shared/client/fill-together.js`](shared/client/fill-together.js) | [`Pages/Examples/FillTogether.cshtml`](Pages/Examples/FillTogether.cshtml) | MIT |
| [Analyze Results with Dashboard](https://surveyjs.io/documentation/backend-integration-aspnet-core#analyze-results-with-dashboard) | [/examples/dashboard](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/dashboard) | [`Endpoints/Dashboard.cs`](Endpoints/Dashboard.cs) | [`shared/client/dashboard.js`](shared/client/dashboard.js) | [`Pages/Examples/Dashboard.cshtml`](Pages/Examples/Dashboard.cshtml) | Commercial client |
| [Load and Save Form JSON Schemas](https://surveyjs.io/documentation/backend-integration-aspnet-core#load-and-save-form-json-schemas) | [/examples/creator-load-save](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/creator-load-save) | [`Endpoints/CreatorLoadSave.cs`](Endpoints/CreatorLoadSave.cs) | [`shared/client/creator-load-save.js`](shared/client/creator-load-save.js) | [`Pages/Examples/CreatorLoadSave.cshtml`](Pages/Examples/CreatorLoadSave.cshtml) | Commercial client |
| [Upload Files](https://surveyjs.io/documentation/backend-integration-aspnet-core#upload-files) | [/examples/creator-uploads](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/creator-uploads) | [`Endpoints/StoreFiles.cs`](Endpoints/StoreFiles.cs) | [`shared/client/creator-uploads.js`](shared/client/creator-uploads.js) | [`Pages/Examples/CreatorUploads.cshtml`](Pages/Examples/CreatorUploads.cshtml) | Commercial client |
| [Translate with AI](https://surveyjs.io/documentation/backend-integration-aspnet-core#translate-with-ai) | [/examples/ai-translation](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/ai-translation) | [`Endpoints/AiTranslation.cs`](Endpoints/AiTranslation.cs) | [`shared/client/ai-translation.js`](shared/client/ai-translation.js) | [`Pages/Examples/AiTranslation.cshtml`](Pages/Examples/AiTranslation.cshtml) | Commercial client, needs an AI provider |
| [Variable Presets](https://surveyjs.io/documentation/backend-integration-aspnet-core#variable-presets) | [/examples/variable-presets](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/variable-presets) | [`Endpoints/VariablePresets.cs`](Endpoints/VariablePresets.cs) | [`shared/client/variable-presets.js`](shared/client/variable-presets.js) | [`Pages/Examples/VariablePresets.cshtml`](Pages/Examples/VariablePresets.cshtml) | Commercial client |
| [Edit Forms Together](https://surveyjs.io/documentation/backend-integration-aspnet-core#edit-forms-together) | [/examples/edit-together](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/edit-together) | [`Endpoints/EditTogether.cs`](Endpoints/EditTogether.cs) | [`shared/client/edit-together.js`](shared/client/edit-together.js) | [`Pages/Examples/EditTogether.cshtml`](Pages/Examples/EditTogether.cshtml) | Commercial client |
| [Validate Responses](https://surveyjs.io/documentation/backend-integration-aspnet-core#validate-responses) | [/examples/validate-response](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/validate-response) | [`Endpoints/ValidateResponse.cs`](Endpoints/ValidateResponse.cs) | [`shared/client/validate-response.js`](shared/client/validate-response.js) | [`Pages/Examples/ValidateResponse.cshtml`](Pages/Examples/ValidateResponse.cshtml) | MIT |
| [Lint JSON Schemas](https://surveyjs.io/documentation/backend-integration-aspnet-core#lint-json-schemas) | [/examples/lint-definition](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/lint-definition) | [`Endpoints/LintDefinition.cs`](Endpoints/LintDefinition.cs) | [`shared/client/lint-definition.js`](shared/client/lint-definition.js) | [`Pages/Examples/LintDefinition.cshtml`](Pages/Examples/LintDefinition.cshtml) | MIT |
| [Generate PDFs](https://surveyjs.io/documentation/backend-integration-aspnet-core#generate-pdfs) | [/examples/server-pdf](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/server-pdf) | [`Endpoints/ServerPdf.cs`](Endpoints/ServerPdf.cs) | [`shared/client/server-pdf.js`](shared/client/server-pdf.js) | [`Pages/Examples/ServerPdf.cshtml`](Pages/Examples/ServerPdf.cshtml) | Commercial |
| [Extract Responses from Documents](https://surveyjs.io/documentation/backend-integration-aspnet-core#extract-responses-from-documents) | [/examples/extract-from-paper](https://surveyjs-aspnet-mvc.demos.surveyjs.io/examples/extract-from-paper) | [`Endpoints/ExtractFromPaper.cs`](Endpoints/ExtractFromPaper.cs) | [`shared/client/extract-from-paper.js`](shared/client/extract-from-paper.js) | [`Pages/Examples/ExtractFromPaper.cshtml`](Pages/Examples/ExtractFromPaper.cshtml) | MIT, needs an AI provider |
<!-- steps:end -->

## Where each example lives

Each example is two or three files you can read on their own. The full explanation is in [the docs](https://surveyjs.io/documentation/backend-integration-aspnet-core).

- **`Endpoints/<PascalSlug>.cs`:** the example's minimal API endpoints, the server code the docs show. Each file is a `MapX()` extension method that `Program.cs` calls in the docs' order.
  - `MapValidateResponse()` replaces `MapSaveResponse()`, and `MapLintDefinition()` replaces `MapCreatorLoadSave()`, when their switches are on.
  - `FillTogether.cs` and `EditTogether.cs` are the two relays, WebSocket endpoints; their rooms are in `Relay/`.
  - The `/claim` page of Load JSON Schemas, Variables, and Answers is a Razor page model: `Pages/Examples/LoadDefinitionVariablesData.cshtml.cs`.
- **`shared/client/<slug>.js`:** the example's client code, a plain ES module, the same file on every platform.
- **`Pages/Examples/<PascalSlug>.cshtml`:** the example's page: its description, the "Try this" list and the component.
- **`Integration/`:** small helpers some endpoints call, so the code the docs show stays about SurveyJS. They aren't demo code:
  - `StoredJson.cs`: the raw-body parsing, the serializer options, `InsertResponseAsync()`, `UpsertAsync()` and the UTC timestamp (see below).
  - `SurveyJsService.cs`: the typed `HttpClient` for the SurveyJS service, with a timeout per call, the 503, 504 and 502 when the service doesn't answer, and the logging.
  - `AiTranslationClient.cs`: the call to your OpenAI-compatible provider.
  - `FileType.cs`: file types by their first bytes.
  - `ApiErrors.cs`: the `{ error }` shape for every error under `/api`.
- **The rest:**
  - `Data/`: the `DbContext`, the entities, the migrations and the seeder.
  - `Authorization/`: the `edit-forms` and `read-file` policies.
  - `Configuration/`: the settings, the port check and the build line.

To copy an example, copy its endpoint file and the helpers it uses. The code the docs show is cut from these files between `#region sjs:<slug>.server` and `#endregion` (and `// #region sjs:<slug>.client` in the client modules). `surveyjs-integration.json` lists every example's files and regions, and `dotnet run --project tools/Check` verifies them.

### Shared files

`shared/` holds what every platform repository uses byte for byte: the client modules, the form definitions, samples and seed data, the page UI (`shared/ui/`), and the contract, smoke and browser tests. It is a copy of [`surveyjs-php-laravel`](https://github.com/surveyjs/surveyjs-php-laravel)'s `shared/` at the commit in `shared/UPSTREAM`, and the check command compares it with `shared/SHA256SUMS`. Don't edit it here: send a fix to that repository, then copy `shared/` again. `.gitattributes` keeps it in LF on Windows checkouts too; otherwise every hash would fail.

### Editing example descriptions

The text in an example's Description panel is the `description` section of `Pages/Examples/<PascalSlug>.cshtml`. It can hold several paragraphs, links and other markup:

```cshtml
@section description {
    <p>Your custom description.</p>
    <p>More details with <strong>formatting</strong>.</p>
}
```

- **The other parts:** the "Try this" list is the `tryThis` section (`try` is a C# keyword), the layout is `Pages/Shared/_ExampleLayout.cshtml`, and the catalog (`Pages/Index.cshtml`) takes each card's "Use when" text from `need` in `surveyjs-integration.json`.
- **Seeing an edit:** restart `dotnet run`, or use `dotnet watch`. If you used a Tailwind class no other page uses, rebuild the stylesheet too (see [Front end](#front-end)).

### Storing JSON exactly as it arrives

A response, a definition, saved progress and variable presets are each one JSON document, stored as text and returned as stored.

- **The raw body, not model binding.** A .NET developer reaches for a bound model there, but a class (or a `Dictionary<string, object>`) drops the keys it doesn't declare and re-formats numbers. The storing endpoints read the raw body with `StoredJson.ReadBodyAsync(request)` and parse it with `JsonNode.Parse`. That keeps every key, `{}`, `[{}]`, `null`, `""`, whitespace inside strings and the number text (`1.0` stays `1.0`). A comment in each endpoint says why. Translate with AI stores nothing, so it binds a record.
- **Written back compactly.** `StoredJson.Options` uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, so `"naïve — 東京"` and `"/api/files/1"` are stored as they are, not as `\u` escapes. These strings go to the database and to JSON answers, never into HTML.
- **Strings, not mapped classes.** The document columns are `string` properties on the entities. There is no `ToJson()`, owned type or complex type, which would map the document to classes.
- **Dates are strings.** `created_at` is ISO 8601 UTC to the second (`2026-11-17T13:36:06Z`, written with `CultureInfo.InvariantCulture`), so the dashboard's date filters are string comparisons.
- **Errors are `{ error }`.** Under `/api`, every error is `{ "error": … }`, also the framework's own: a body that isn't JSON is a `400`, and so are a bad query value (`limit=abc`), an unknown route (`404`), a wrong method (`405`), a JSON post to an upload endpoint (`415`) and an unexpected exception (`500 { "error": "Internal server error" }`). The error writer is `Integration/ApiErrors.cs`; nothing answers with ProblemDetails.

### Database

The examples store their tables in SQLite (`storage/database.sqlite`) through [Entity Framework Core](https://learn.microsoft.com/ef/core/).

- **The data layer.** There is one entity class per table (`Data/Entities.cs`), with `[Table]` and `[Column]` attributes for the names the docs use. `Data/SurveyDb.cs` is the `DbContext`, and the committed migrations in `Data/Migrations/` create the tables on start (`MigrateAsync()`). `Data/Seeder.cs`, EF Core's seeding hook, seeds them from `shared/seed/`. It adds missing forms on every start, and fills the other tables only when they are empty.
- **Queries.** The endpoints use LINQ over the `DbSet`s. A field inside a stored document is read with SQLite's `json_extract`, mapped as `SurveyDb.JsonValue(r.Data, "$.customer_email")`. Writes of one document under a key go through `StoredJson.UpsertAsync()`, one `INSERT … ON CONFLICT` statement, because EF Core has no upsert. The only SQL in the code is `ExecuteSql` / `FromSql` with interpolated parameters, never their `Raw` forms.
- **After changing an entity**, add a migration and commit it. A test fails when the model and the migrations differ.

  ```bash
  dotnet tool restore
  dotnet ef migrations add <Name> --output-dir Data/Migrations
  ```

- **Look inside** with the `sqlite3` command-line shell:

  ```bash
  sqlite3 storage/database.sqlite "SELECT id, form_id, data FROM responses ORDER BY id DESC LIMIT 5"
  ```

  The `-wal` and `-shm` files next to it belong to the database (EF Core opens SQLite in WAL mode). Stop the app and delete `storage/` to start over: the next start creates and seeds everything again.
- **Not EF Core?** Plain `Microsoft.Data.Sqlite` or Dapper with a `TEXT` column works just as well; keep the documents as text.

## How data works in the demo

Locally, everyone shares one database, `storage/database.sqlite`. Delete `storage/` to start over.

On a hosted demo (`DEMO_MODE=true`), each visitor gets a private copy of the seeded database and their own uploads folder:

- **The sandbox.** A `demo_sid` cookie picks it. It is created on first use (`SqliteConnection.BackupDatabase`, which also copies what is still in the `-wal` file), and kept until it has been unused for 24 hours. The cleanup is a `BackgroundService` in the app, `Demo/DemoSandbox.cs`.
- **Sharing.** The "Open a second window" link in Fill Forms Together and Edit Forms Together carries that id (`?join=`), so whoever opens it works in the same copy and the same relay rooms.
- **Limits.** The demo caps uploads at 5 MB per file and the AI examples at 20 calls per visitor per day, with ASP.NET Core's rate limiter.
- **Not in the example code.** The `DemoSandbox` middleware points a scoped `RequestStorage` service at the visitor's files. `SurveyDb` and the upload endpoints read it, so the endpoints don't change.

## Front end

- **The pages.** They are Razor Pages (`Pages/`), HTML-encoded by default. Each page diffs against its Blade source in the reference repository only in template syntax, with the Tailwind class strings unchanged. Inline JSON (the page config, `window.FORM_DEFINITION`, `window.FORM_VARIABLES`) is written with `@Json.Serialize(…)`, which escapes `<`, `>` and `&`, so a `</script>` in a definition can't end the script. No value from the database is ever `@Html.Raw`.
- **The stylesheet.** It is Tailwind CSS v4 without Preflight.
  - `Styles/site.css` imports `tailwindcss/theme.css` (static), `tailwindcss/utilities.css` and `shared/ui/theme.css`.
  - `npm run css` builds it into `wwwroot/css/site.css` with `@tailwindcss/cli`. The output is committed, so `dotnet run` never needs Node; CI rebuilds it and fails on a difference.
  - Rebuild it after editing a page. The [Tailwind standalone binary](https://tailwindcss.com/blog/standalone-cli) builds the same file without Node: `tailwindcss -i Styles/site.css -o wwwroot/css/site.css --minify`.
- **SurveyJS comes from the NuGet packages** (`SurveyJS`, `SurveyJS.Creator`, `SurveyJS.Analytics`).
  - ASP.NET Core serves their ES module builds as static web assets under `/_content/SurveyJS.*/`, with npm's paths: `/_content/SurveyJS.Core/fesm/survey-core.mjs` is npm's `survey-core/fesm/survey-core.mjs`.
  - The shared modules in `shared/client/` and `shared/ui/` load unchanged with `<script type="module">` and an import map (`Demo/ImportMap.cs`), which points each SurveyJS specifier at those files. At start, the app checks that each one exists. The ES module builds are in the packages from SurveyJS 3.2.6; an older version stops the app with the missing package file.
- **The third-party modules come from unpkg.** `chart.js` and `@kurkle/color` (which `survey-analytics` imports), `lodash-es` and highlight.js are in no SurveyJS package, so the import map points them at unpkg at pinned versions. Self-host them by copying their files into `wwwroot/` and changing the URLs in `Demo/ImportMap.cs`.
- **Don't mix the two builds.** Don't put the packages' script-tag builds (`survey.core.min.js` and friends) and the ES modules on one page: they would load SurveyJS twice.
- **Highlighting.** The Code tab highlights the C# regions with highlight.js's `csharp` language. The import map has an entry for every language `shared/ui/code-highlight.js` imports.
- **Theming.** The forms use the SurveyJS Tailwind adapter (`survey-core/themes/adapters/tailwind4.min.css`). No page calls `applyTheme()`: its inline styles would override the adapter.

## Not on minimal APIs?

The endpoints only use the request, the database and the response, so they port in a few lines. Save Responses as an MVC controller action, reading the raw body for the same reason:

```csharp
[ApiController]
public class ResponsesController(SurveyDb db) : ControllerBase
{
    [HttpPost("/api/responses")]
    public async Task<IActionResult> Save()
    {
        // Read the raw request body: binding to a class would drop unknown keys and re-format numbers such as 1.0
        if (await StoredJson.ReadBodyAsync(Request) is not JsonObject body
            || body["formId"] is not JsonValue formId || body["data"] is not JsonNode data)
        {
            return BadRequest(new { error = "Expected { formId, data }" });
        }
        var id = await StoredJson.InsertResponseAsync(db, formId.ToString(), data);
        return StatusCode(201, new { id });
    }
}
```

Only the request and response wrappers change.

## Disclaimer

These examples must not be used as a real service as they are. They don't cover authentication, authorization, user management, access levels and other security aspects of a real survey service. The places where real checks go are marked:

- **`Authorization/EditFormsRequirement.cs`:** the `edit-forms` policy, which decides who may save definitions and presets and join the edit-together relay.
- **`Authorization/ReadFileHandler.cs`:** the `read-file` policy, which decides who may read an uploaded file.
- **`Demo/DemoUsers.cs`:** the demo authentication handler. It reads a `demo_user` cookie; your app authenticates there with cookie or JWT authentication (`AddCookie()`, `AddJwtBearer()`).
- **Antiforgery.** The two upload endpoints call `.DisableAntiforgery()` because the pages call them with `fetch()` from the same origin. With cookie authentication, validate an antiforgery token or send an `Authorization` header instead.

When you copy an example into your app, leave out the demo-only code:

- the `Demo/` folder, and its lines in `Program.cs`;
- `Relay/RoomKey.cs`'s sandbox keys;
- `Pages/`, `shared/ui/` (the page frame, its tabs and highlighting, the fonts and site images) and `shared/client/host.js`. Its `mountSurvey()` is one line: `renderSurvey(survey, element)` from `survey-js-ui`.

## Licence

The code in this repository is available under the [MIT licence](LICENSE).

The examples use SurveyJS Form Library, which is MIT-licensed as well. The examples marked "Commercial" in the Licence column above also use SurveyJS Survey Creator, Dashboard or PDF Generator, which are commercial products. You can integrate them and evaluate their functionality without a licence. Production use requires a developer licence for each developer who works with the SurveyJS APIs or implements the integration. See the [SurveyJS Licensing FAQ](https://surveyjs.io/faq#licensing) and the [End-User License Agreement](https://surveyjs.io/Developer-License-Agreement.pdf).

## Other Resources

The [Endatix Platform](https://endatix.com/?utm_source=surveyjs&utm_medium=partner+link) is an open-source solution that provides a fully integrated server-side API for managing SurveyJS forms and their results within .NET environments. Endatix provides a complete backend application structure, from API requests to database integration and admin management, allowing users to quickly implement a fully operational server environment with minimal coding. Endatix is an ideal choice for developers looking for an efficient way to implement SurveyJS without extensive server-side development.

**Endatix Platform repo:** https://github.com/endatix/endatix
