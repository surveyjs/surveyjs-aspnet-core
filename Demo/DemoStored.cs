using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SurveyJsAspNetCore.Configuration;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;
using SurveyJsAspNetCore.Relay;

namespace SurveyJsAspNetCore.Demo;

/// <summary>
/// Demo only: GET /demo/stored/{slug}, "What the server stored". A fixed, whitelisted read per example, never arbitrary
/// SQL. The page sets ?id=, ?key= or ?room= as it needs them.
/// </summary>
public static partial class DemoStored
{
    public static IEndpointRouteBuilder MapDemoStored(this IEndpointRouteBuilder app)
    {
        app.MapGet("/demo/stored/{slug}", async (string slug, HttpContext context, SurveyDb db, RequestStorage storage,
            IMemoryCache cache, IOptions<SurveyJsOptions> options) =>
        {
            var reader = new Reader(db, context.User, context.Request.Query, storage, cache, options.Value);
            var stored = await reader.StoredAsync(slug);
            return stored is null
                ? Results.Json(new { error = "Not Found" }, statusCode: 404)
                : Results.Json(stored, StoredJson.Options);
        });
        return app;
    }

    private sealed class Reader(SurveyDb db, ClaimsPrincipal user, IQueryCollection query, RequestStorage storage, IMemoryCache cache, SurveyJsOptions options)
    {
        private readonly long? id = long.TryParse(query["id"], out var value) && value > 0 ? value : null;

        private string Key(string fallback) => query["key"].FirstOrDefault() ?? fallback;

        public async Task<JsonObject?> StoredAsync(string slug) => slug switch
        {
            "save-response" => new() { ["responses"] = await ResponsesAsync(id is null ? "contact" : null) },
            "load-definition-variables-data" => new()
            {
                ["responses"] = await ResponsesAsync("claim", customerEmail: id is null ? user.FindFirstValue(ClaimTypes.Email) ?? "" : null),
            },
            "resume-progress" => new()
            {
                ["progress"] = await DocumentAsync(db.Progress.Where(p => p.Key == $"{user.FindFirstValue(ClaimTypes.NameIdentifier)}:{Key("onboarding")}")
                    .Select(p => new KeyJson(p.Key, p.Json))),
                ["responses"] = await ResponsesAsync("onboarding"),
            },
            "store-files" => new() { ["responses"] = await ResponsesAsync("expenses", limit: 3), ["files"] = await FilesAsync(10) },
            "choices-from-web" => new()
            {
                ["responses"] = await ResponsesAsync("visit", limit: 3),
                ["offices"] = Array(await db.Offices.OrderBy(o => o.Region).ThenBy(o => o.Name).ToListAsync()),
                ["countries cache"] = cache.TryGetValue("countries", out _) ? "cached for 24 hours" : "empty: the next request fetches restcountries.com",
            },
            "async-functions" => new()
            {
                ["customers"] = Array(await db.Customers.Select(c => c.Email).ToListAsync()),
                ["shipping_rates"] = Array(await db.ShippingRates.OrderBy(r => r.PostcodePrefix)
                    .Select(r => new { postcode_prefix = r.PostcodePrefix, price = r.Price }).ToListAsync()),
            },
            "relational-storage" => await ClaimsAsync(),
            "fill-together" => new() { ["relay room"] = RelaySnapshot("fill"), ["responses"] = await ResponsesAsync("job-sheet", limit: 3) },
            "dashboard" => new() { ["responses"] = await db.Responses.CountAsync(r => r.FormId == "feedback") },
            "creator-load-save" => new() { ["forms"] = await FormAsync(Key("support")) },
            "ai-translation" => new() { ["forms"] = await FormAsync("support-translations") },
            "creator-uploads" => new() { ["forms"] = await FormAsync("support-assets"), ["files"] = await FilesAsync(5) },
            "variable-presets" => new()
            {
                ["variable_presets"] = await DocumentAsync(db.VariablePresets.Where(p => p.Key == Key("support")).Select(p => new KeyJson(p.Key, p.Json))),
            },
            "edit-together" => new() { ["relay room"] = RelaySnapshot("edit"), ["forms"] = await FormAsync(Key("support-team")) },
            "validate-response" => new() { ["responses"] = await ResponsesAsync("claim") },
            "lint-definition" => new() { ["forms"] = await FormAsync("support-lint") },
            "server-pdf" => new() { ["responses"] = await ResponsesAsync("claim", limit: 10) },
            "extract-from-paper" => new() { ["row counts"] = await RowCountsAsync(), ["responses"] = await ResponsesAsync("work-order", limit: 3) },
            _ => null,
        };

        private async Task<JsonArray> ResponsesAsync(string? formId, int limit = 5, string? customerEmail = null)
        {
            var rows = db.Responses.AsQueryable();
            if (formId is not null) rows = rows.Where(r => r.FormId == formId);
            if (id is not null) rows = rows.Where(r => r.Id == id);
            if (customerEmail is not null) rows = rows.Where(r => SurveyDb.JsonValue(r.Data, "$.customer_email") == customerEmail);
            var list = await rows.OrderByDescending(r => r.Id).Take(limit).ToListAsync();
            return new JsonArray(list.Select(r => (JsonNode)new JsonObject
            {
                ["data"] = JsonNode.Parse(r.Data),
                ["id"] = r.Id,
                ["form_id"] = r.FormId,
                ["definition_version"] = r.DefinitionVersion,
                ["created_at"] = r.CreatedAt,
            }).ToArray());
        }

        private Task<JsonNode?> FormAsync(string key) =>
            DocumentAsync(db.Forms.Where(f => f.Key == key).Select(f => new KeyJson(f.Key, f.Json)));

        private static async Task<JsonNode?> DocumentAsync(IQueryable<KeyJson> query)
        {
            var row = await query.SingleOrDefaultAsync();
            return row is null ? null : new JsonObject { ["key"] = row.Key, ["json"] = JsonNode.Parse(row.Json) };
        }

        /// <summary>The latest files, newest first: SQLite's rowid is the insertion order.</summary>
        private async Task<JsonArray> FilesAsync(int limit) =>
            Array(await db.Files.FromSql($"SELECT * FROM files ORDER BY rowid DESC LIMIT {limit}")
                .Select(f => new { id = f.Id, name = f.Name, type = f.Type }).ToListAsync());

        private async Task<JsonObject> ClaimsAsync()
        {
            var rows = from claim in db.Claims
                       join response in db.Responses on claim.ResponseId equals response.Id
                       where id == null || response.Id == id
                       orderby claim.Id descending
                       select new { claim, response };
            var list = await rows.Take(3).ToListAsync();
            return new JsonObject
            {
                ["claims"] = Array(list.Select(r => new
                {
                    claim_id = r.claim.Id, response_id = r.claim.ResponseId, customer_email = r.claim.CustomerEmail, amount = r.claim.Amount,
                })),
                ["responses"] = new JsonArray(list.Select(r => (JsonNode)new JsonObject
                {
                    ["id"] = r.response.Id,
                    ["form_id"] = r.response.FormId,
                    ["definition_version"] = r.response.DefinitionVersion,
                    ["created_at"] = r.response.CreatedAt,
                    ["data"] = JsonNode.Parse(r.response.Data),
                }).ToArray()),
            };
        }

        private JsonNode? RelaySnapshot(string relay)
        {
            var room = query["room"].FirstOrDefault() ?? "";
            if (!RoomId().IsMatch(room))
            {
                return null;
            }
            if (!options.DemoMode)
            {
                return "The relay writes room snapshots in demo mode only (DEMO_MODE=true)";
            }
            var path = RoomKey.SnapshotPath(options.DataDir, RoomKey.For(storage, room), relay);
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : "No room data";
        }

        private async Task<JsonObject> RowCountsAsync() => new()
        {
            ["forms"] = await db.Forms.CountAsync(),
            ["responses"] = await db.Responses.CountAsync(),
            ["progress"] = await db.Progress.CountAsync(),
            ["files"] = await db.Files.CountAsync(),
            ["claims"] = await db.Claims.CountAsync(),
            ["variable_presets"] = await db.VariablePresets.CountAsync(),
        };

        private static JsonArray Array<T>(IEnumerable<T> items) =>
            System.Text.Json.JsonSerializer.SerializeToNode(items, StoredJson.Options)!.AsArray();
    }

    private sealed record KeyJson(string Key, string Json);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex RoomId();
}
