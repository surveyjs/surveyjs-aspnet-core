using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SurveyJsAspNetCore.Data;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>Analyze Results with Dashboard: GET /api/responses returns the stored response documents.</summary>
public static class Dashboard
{
    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder app)
    {
        #region sjs:dashboard.server
        // GET /api/responses?formId=&includeCreatedAt=&from=&to=&limit=&offset=
        app.MapGet("/api/responses", async (string? formId, string? from, string? to, string? includeCreatedAt, int? limit, int? offset, SurveyDb db) =>
        {
            var query = db.Responses.Where(r => r.FormId == formId
                && string.Compare(r.CreatedAt, from ?? "1970-01-01") >= 0);               // ISO-8601 UTC strings compare as dates
            if (!string.IsNullOrEmpty(to))
                query = query.Where(r => string.Compare(r.CreatedAt.Substring(0, 10), to) <= 0);   // up to and including that day
            var rows = await query.OrderBy(r => r.CreatedAt)
                .Skip(Math.Max(offset ?? 0, 0)).Take(Math.Clamp(limit ?? 500, 1, 5000))   // filter or page on the server for large volumes
                .Select(r => new { r.Data, r.CreatedAt }).ToListAsync();

            // Include submission dates separately when requested by Dashboard
            if (includeCreatedAt is "1" or "true")
                return Results.Json(rows.Select(r => new { data = JsonNode.Parse(r.Data), createdAt = r.CreatedAt }));

            return Results.Text("[" + string.Join(",", rows.Select(r => r.Data)) + "]", "application/json");   // documents as stored, not re-encoded
        });
        #endregion
        return app;
    }
}
