using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SurveyJsAspNetCore.Data;

namespace SurveyJsAspNetCore.Integration;

/// <summary>The tables that hold one JSON document under a key; UpsertAsync picks its SQL from this fixed list.</summary>
public enum StoredTable { Forms, Progress, VariablePresets }

/// <summary>
/// Stores SurveyJS JSON (responses, definitions, saved progress, variable presets) exactly as it arrived. Used by
/// save-response, resume-progress, relational-storage, creator-load-save, variable-presets, validate-response,
/// lint-definition and the fill-together relay. A response or a definition is one JSON document: keep it as text, never
/// as a class. When you copy one of those examples, copy this file too.
/// </summary>
public static class StoredJson
{
    /// <summary>
    /// How stored documents are written:
    /// - UnsafeRelaxedJsonEscaping: "naïve — 東京" and "/api/files/1" are stored as they are, not as \u escapes
    ///   (the default encoder escapes for HTML; these strings go to the database and to JSON answers, never into HTML)
    /// - no indentation: one document per row, as compact as it arrived
    /// JsonNode keeps {} as {}, [{}] as [{}], null, "" and the number text, so 1.0 stays 1.0.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>
    /// The request body as a JSON tree, read from the raw body: model binding to a class would drop the keys the class
    /// doesn't have and re-format numbers. Body text that isn't JSON ends the request with
    /// 400 { error: "The request body is not valid JSON" } (Integration/ApiErrors.cs).
    /// </summary>
    public static async Task<JsonNode?> ReadBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        var text = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            throw new ErrorResponseException(StatusCodes.Status400BadRequest, "The request body is not valid JSON");
        }
    }

    /// <summary>The document as stored: compact, with the encoder above.</summary>
    public static string Encode(JsonNode? document) => document is null ? "null" : document.ToJsonString(Options);

    /// <summary>
    /// Insert one response: the answers as one JSON document. relational-storage adds definition_version.
    /// The caller has checked the envelope; formId and data are stored as sent.
    /// </summary>
    public static async Task<long> InsertResponseAsync(SurveyDb db, string formId, JsonNode data, string? definitionVersion = null)
    {
        var response = new StoredResponse
        {
            FormId = formId,
            DefinitionVersion = definitionVersion,
            Data = Encode(data),
            CreatedAt = Now(),
        };
        db.Responses.Add(response);
        await db.SaveChangesAsync();
        return response.Id;
    }

    /// <summary>
    /// Insert or replace the JSON document stored under a key (forms, progress, variable_presets). One SQL statement,
    /// because EF Core has no upsert and a find-then-add would let two simultaneous PUTs collide on the key.
    /// The table comes from the fixed list above, never from the request; the key and the JSON are parameters.
    /// </summary>
    public static async Task UpsertAsync(SurveyDb db, StoredTable table, string key, JsonNode? document)
    {
        var json = Encode(document);
        _ = table switch
        {
            StoredTable.Forms => await db.Database.ExecuteSqlAsync(
                $"INSERT INTO forms (key, json) VALUES ({key}, {json}) ON CONFLICT (key) DO UPDATE SET json = excluded.json"),
            StoredTable.Progress => await db.Database.ExecuteSqlAsync(
                $"INSERT INTO progress (key, json) VALUES ({key}, {json}) ON CONFLICT (key) DO UPDATE SET json = excluded.json"),
            StoredTable.VariablePresets => await db.Database.ExecuteSqlAsync(
                $"INSERT INTO variable_presets (key, json) VALUES ({key}, {json}) ON CONFLICT (key) DO UPDATE SET json = excluded.json"),
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
    }

    /// <summary>ISO 8601 UTC with a Z, culture-independent: these strings sort and compare as dates.</summary>
    public static string Now() => Iso(DateTime.UtcNow);

    public static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
