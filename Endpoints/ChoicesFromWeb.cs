using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SurveyJsAspNetCore.Data;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>Load Choices from Web: GET /api/offices?region= and GET /api/countries.</summary>
public static class ChoicesFromWeb
{
    public static IEndpointRouteBuilder MapChoicesFromWeb(this IEndpointRouteBuilder app)
    {
        #region sjs:choices-from-web.server
        // GET /api/offices?region= — {region} from the form arrives as a query parameter
        app.MapGet("/api/offices", async (string? region, SurveyDb db) =>
            await db.Offices.Where(o => o.Region == (region ?? "")).OrderBy(o => o.Name).Select(o => new { o.Id, o.Name }).ToListAsync());

        // GET /api/countries — a proxy: the browser can't call a service without CORS, your server can
        app.MapGet("/api/countries", async (IMemoryCache cache, IHttpClientFactory http, ILoggerFactory logs, IWebHostEnvironment env, HttpResponse response) =>
        {
            response.Headers.CacheControl = "public, max-age=86400";
            try
            {
                return await cache.GetOrCreateAsync("countries", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
                    var all = await http.CreateClient("restcountries").GetFromJsonAsync<RestCountry[]>("https://restcountries.com/v3.1/all?fields=name,cca2");
                    return all!.Select(c => new Country(c.Cca2, c.Name.Common)).OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
                });
            }
            catch (Exception e)   // down, or a different answer: serve the bundled list for 10 minutes, so requests don't each wait for the timeout
            {
                logs.CreateLogger("ChoicesFromWeb").LogWarning(e, "restcountries.com failed; serving shared/seed/countries.json");
                return cache.Set("countries", BundledCountries(env), TimeSpan.FromMinutes(10));
            }
        });
        #endregion
        return app;
    }

    private sealed record Country([property: JsonPropertyName("code")] string Code, [property: JsonPropertyName("name")] string Name);

    private sealed record RestCountry(string Cca2, RestCountryName Name);

    private sealed record RestCountryName(string Common);

    private static List<Country> BundledCountries(IWebHostEnvironment env) =>
        JsonSerializer.Deserialize<List<Country>>(File.ReadAllText(Path.Combine(env.ContentRootPath, "shared", "seed", "countries.json")))!;
}
