using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;

namespace SurveyJsAspNetCore.Demo;

/// <summary>
/// Demo only: adds X-Demo-Cache: hit / miss / fallback to GET /api/countries, so the requests log shows where the list
/// came from. An endpoint filter outside the example's file: it checks whether the cache entry existed before the
/// handler ran, and compares the answer with the bundled shared/seed/countries.json ("fallback" wins, also while the
/// bundled list is cached).
/// </summary>
public sealed class DemoCacheHeader(IMemoryCache cache, IWebHostEnvironment env) : IEndpointFilter
{
    private readonly Lazy<JsonNode?> bundled = new(() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(env.ContentRootPath, "shared", "seed", "countries.json"))));

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Request.Path.Equals("/api/countries", StringComparison.OrdinalIgnoreCase))
        {
            return await next(context);
        }
        var cached = cache.TryGetValue("countries", out _);
        var result = await next(context);
        var value = result is IValueHttpResult { Value: { } answer } ? answer : result;
        var fallback = JsonNode.DeepEquals(JsonSerializer.SerializeToNode(value, JsonSerializerOptions.Web), bundled.Value);
        context.HttpContext.Response.Headers["X-Demo-Cache"] = fallback ? "fallback" : cached ? "hit" : "miss";
        return result;
    }
}
