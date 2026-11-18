using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>Generate PDFs: GET /api/claims/{id}/pdf, printed by the SurveyJS service.</summary>
public static class ServerPdf
{
    public static IEndpointRouteBuilder MapServerPdf(this IEndpointRouteBuilder app)
    {
        #region sjs:server-pdf.server
        // GET /api/claims/{id}/pdf — ask the SurveyJS service for the filled form and pass the PDF through
        app.MapGet("/api/claims/{id:long}/pdf", async (long id, SurveyDb db, SurveyJsService service, IWebHostEnvironment env, HttpResponse response) =>
        {
            var claim = await db.Responses.SingleOrDefaultAsync(r => r.FormId == "claim" && r.Id == id);
            if (claim is null) return Results.Json(new { error = "Unknown claim" }, statusCode: 404);
            // Use the schema version recorded with the response
            if (claim.DefinitionVersion is not (null or "v1" or "v2"))
                return Results.Json(new { error = "The schema version for this claim is unavailable" }, statusCode: 409);
            var definition = claim.DefinitionVersion is null
                ? await db.Forms.Where(f => f.Key == "claim").Select(f => f.Json).SingleAsync()
                : await File.ReadAllTextAsync(Path.Combine(env.ContentRootPath, "shared", "definitions", $"relational-storage.{claim.DefinitionVersion}.json"));

            var pdf = await service.PostAsync("/pdf", new { schema = JsonNode.Parse(definition), response = JsonNode.Parse(claim.Data) }, TimeSpan.FromSeconds(60));
            if (pdf.Status != 200 || !pdf.HasContentType("application/pdf") || !pdf.BodyStartsWith("%PDF-"))
                throw service.Unexpected("/pdf", pdf);

            response.Headers.ContentDisposition = $"inline; filename=\"claim-{id}.pdf\"";
            response.Headers.CacheControl = "private, no-store";
            return Results.Bytes(pdf.Body, "application/pdf");
        });
        #endregion
        return app;
    }
}
