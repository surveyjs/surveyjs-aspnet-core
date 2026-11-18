using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>
/// Extract Responses from Documents: the blank work order (GET /api/work-orders/blank.pdf) and the extraction
/// (POST /api/work-orders/extract), both through the SurveyJS service and the stored work-order form.
/// </summary>
public static class ExtractFromPaper
{
    public static IEndpointRouteBuilder MapExtractFromPaper(this IEndpointRouteBuilder app)
    {
        #region sjs:extract-from-paper.template
        // Generate a blank, fillable PDF using the same schema as extraction
        app.MapGet("/api/work-orders/blank.pdf", async (SurveyDb db, SurveyJsService service, HttpResponse response) =>
        {
            var definition = await db.Forms.Where(f => f.Key == "work-order").Select(f => f.Json).SingleOrDefaultAsync();
            if (definition is null) return Results.Json(new { error = "Unknown work order form" }, statusCode: 404);
            var pdf = await service.PostAsync("/pdf", new { schema = JsonNode.Parse(definition) }, TimeSpan.FromSeconds(60));
            if (pdf.Status != 200 || !pdf.HasContentType("application/pdf") || !pdf.BodyStartsWith("%PDF-"))
                throw service.Unexpected("/pdf", pdf);

            response.Headers.ContentDisposition = "attachment; filename=\"work-order.pdf\"";
            response.Headers.CacheControl = "private, no-store";
            return Results.Bytes(pdf.Body, "application/pdf");
        });
        #endregion

        #region sjs:extract-from-paper.server
        // POST /api/work-orders/extract — send the uploaded scan to the SurveyJS service; return a draft response
        app.MapPost("/api/work-orders/extract", async (IFormFile? scan, SurveyDb db, SurveyJsService service, CancellationToken aborted) =>
        {
            if (scan is null) return Results.Json(new { error = "Upload the scan in a field named \"scan\"" }, statusCode: 400);
            if (scan.Length > 5 * 1024 * 1024) return Results.Json(new { error = "The scan must be a file of 5 MB or less" }, statusCode: 413);
            var bytes = await FileType.ReadAllBytesAsync(scan, aborted);
            if (FileType.Detect(bytes) is null)                                       // its first bytes, not the client's content type
                return Results.Json(new { error = "The file is not a PDF, PNG, JPEG, WebP or GIF document" }, statusCode: 400);
            var definition = await db.Forms.Where(f => f.Key == "work-order").Select(f => f.Json).SingleAsync();
            var output = await service.PostAsync("/extract",                          // JSON with base64, not multipart
                new { schema = JsonNode.Parse(definition), document = Convert.ToBase64String(bytes) }, TimeSpan.FromSeconds(120));

            return output switch                                                      // nothing is saved: a person reviews the draft
            {
                { Status: 200, Object: { } result } when result["data"] is JsonObject data =>
                    Results.Json(new { answers = data, confidence = result["confidence"] ?? new JsonArray(), uniqueId = result["uniqueId"] }),
                { Status: 400, Error: "INVALID_DOCUMENT" } => Results.Json(new { error = "The file is not a PDF, PNG, JPEG, WebP or GIF document" }, statusCode: 400),
                { Status: 503, Error: "AI_NOT_CONFIGURED" } => Results.Json(new { error = "The SurveyJS service has no AI provider configured" }, statusCode: 503),
                { Status: 502, Error: "EXTRACTION_FAILED" } => Results.Json(new { error = "Could not extract the response from the document" }, statusCode: 502),
                _ => throw service.Unexpected("/extract", output, logBody: false),
            };
        }).DisableAntiforgery();   // called with fetch() from the same origin; with cookie auth, validate a token or use an Authorization header
        #endregion
        return app;
    }
}
