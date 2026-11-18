using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace SurveyJsAspNetCore.Pages.Examples;

/// <summary>Generate PDFs on the Server. The page lists the latest claims, each with its saved answers.</summary>
public class ServerPdfModel : ExampleModel
{
    public sealed record ClaimRow(long Id, string Email, string Answers);

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        IndentSize = 4,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The ten most recent claim responses, newest first.</summary>
    public async Task<List<ClaimRow>> LatestClaimsAsync()
    {
        var rows = await Db.Responses.Where(r => r.FormId == "claim").OrderByDescending(r => r.Id).Take(10)
            .Select(r => new { r.Id, r.Data }).ToListAsync();
        return rows.Select(r =>
        {
            var answers = JsonNode.Parse(r.Data);
            return new ClaimRow(r.Id, Email(answers), answers?.ToJsonString(Pretty) ?? "null");
        }).ToList();
    }

    // Any scalar customer_email is shown as text; anything else (missing, null, an object) is "No email provided"
    private static string Email(JsonNode? answers) => answers is JsonObject data && data["customer_email"] is JsonValue email
        ? email.GetValueKind() switch
        {
            JsonValueKind.String => email.GetValue<string>(),
            JsonValueKind.True => "1",
            JsonValueKind.False => "",
            _ => email.ToJsonString(),
        }
        : "No email provided";
}
