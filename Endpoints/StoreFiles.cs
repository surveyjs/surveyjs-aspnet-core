using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>Store Files Separately: POST /api/files and GET /api/files/{id}. creator-uploads uses them too.</summary>
public static class StoreFiles
{
    public static IEndpointRouteBuilder MapStoreFiles(this IEndpointRouteBuilder app)
    {
        #region sjs:store-files.server
        // POST /api/files — store uploads, return their ids in the same order
        app.MapPost("/api/files", async (IFormFileCollection form, SurveyDb db, RequestStorage storage, CancellationToken aborted) =>
        {
            var files = form.GetFiles("files[]");                                  // field "files[]": one file or many
            if (files.Count == 0) return Results.Json(new { error = "Upload one or more files in a field named \"files[]\"" }, statusCode: 422);
            var uploads = new List<(string Id, string Name, byte[] Bytes, string Type)>();
            foreach (var file in files)                                             // check the whole batch before storing any of it
            {
                if (file.Length > 5 * 1024 * 1024) return Results.Json(new { error = "Each file must be 5 MB or less" }, statusCode: 422);
                var bytes = await FileType.ReadAllBytesAsync(file, aborted);
                if (FileType.Detect(bytes) is not { } type)                         // the detected type, never the client's
                    return Results.Json(new { error = $"{file.FileName} is not a PNG, JPEG, WebP, GIF or PDF file" }, statusCode: 422);
                uploads.Add((Guid.NewGuid().ToString(), file.FileName, bytes, type));
            }
            foreach (var upload in uploads)                                         // named by id on disk, never by the client
            {
                await File.WriteAllBytesAsync(Path.Combine(storage.UploadsPath, upload.Id), upload.Bytes, aborted);
                db.Files.Add(new StoredFile { Id = upload.Id, Name = upload.Name, Type = upload.Type });
            }
            await db.SaveChangesAsync(aborted);
            return Results.Json(uploads.Select(upload => upload.Id));              // the ids, in upload order
        }).DisableAntiforgery();   // called with fetch() from the same origin; with cookie auth, validate a token or use an Authorization header
        #endregion

        #region sjs:store-files.download
        // GET /api/files/{id} — the content, for private files only; check access first
        app.MapGet("/api/files/{id}", async (string id, ClaimsPrincipal user, IAuthorizationService auth, SurveyDb db, RequestStorage storage, HttpResponse response) =>
        {
            var file = await db.Files.FindAsync(id);
            if (file is null || !(await auth.AuthorizeAsync(user, file, "read-file")).Succeeded)
                return Results.Json(new { error = "Not Found" }, statusCode: 404);    // 404, not 403: don't confirm the file exists

            response.Headers.XContentTypeOptions = "nosniff";                       // nosniff and sandbox: an uploaded HTML or SVG
            response.Headers.ContentSecurityPolicy = "sandbox";                     // never runs as this site
            return Results.File(Path.Combine(storage.UploadsPath, id), file.Type);
        });
        #endregion
        return app;
    }
}
