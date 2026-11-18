using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Data;

/// <summary>
/// Seeds the demo users and every example's data from shared/seed/*.json, the same files the other platform
/// repositories seed from. EF Core runs it after migrating (UseSeeding / UseAsyncSeeding in SurveyDbSetup). It is
/// idempotent: it adds the forms that are missing, and fills the other tables only when they are empty. Documents are
/// stored as the seed's JSON text re-serialized with StoredJson.Options, never through a typed model.
/// </summary>
public sealed class Seeder(string sharedRoot)
{
    /// <summary>For `dotnet ef database update`, which calls the synchronous hook.</summary>
    public void Seed(SurveyDb db) => SeedAsync(db, CancellationToken.None).GetAwaiter().GetResult();

    public async Task SeedAsync(SurveyDb db, CancellationToken cancellation)
    {
        // Every form key in forms.json: a deleted or newly added form comes back on the next start
        var forms = Read("seed/forms.json").AsObject();
        var existing = await db.Forms.Select(f => f.Key).ToListAsync(cancellation);
        foreach (var (key, file) in forms)
        {
            if (!existing.Contains(key))
            {
                db.Forms.Add(new Form { Key = key, Json = StoredJson.Encode(Read($"definitions/{(string)file!}")) });
            }
        }
        await db.SaveChangesAsync(cancellation);

        // Demo only: the users the pages switch between (Demo/DemoUsers.cs)
        if (!await db.Users.AnyAsync(cancellation))
        {
            db.Users.AddRange(
                new User { Key = "alice", Name = "Alice Martin", Email = "alice@example.com", Plan = "premium", IsEditor = true },
                new User { Key = "bob", Name = "Bob Fischer", Email = "bob@example.com", Plan = "basic", IsEditor = false });
            await db.SaveChangesAsync(cancellation);
        }

        if (!await db.Offices.AnyAsync(cancellation))
        {
            foreach (var office in Read("seed/offices.json").AsArray())
            {
                db.Offices.Add(new Office { Id = (string)office!["id"]!, Name = (string)office["name"]!, Region = (string)office["region"]! });
            }
        }
        if (!await db.Customers.AnyAsync(cancellation))
        {
            foreach (var email in Read("seed/customers.json").AsArray())
            {
                db.Customers.Add(new Customer { Email = (string)email! });
            }
        }
        if (!await db.ShippingRates.AnyAsync(cancellation))
        {
            foreach (var rate in Read("seed/shipping-rates.json").AsArray())
            {
                db.ShippingRates.Add(new ShippingRate { PostcodePrefix = (string)rate!["postcode_prefix"]!, Price = (decimal)rate["price"]! });
            }
        }
        if (!await db.VariablePresets.AnyAsync(cancellation))
        {
            foreach (var (formId, presets) in Read("seed/variable-presets.json").AsObject())
            {
                db.VariablePresets.Add(new VariablePreset { Key = formId, Json = StoredJson.Encode(presets) });
            }
        }
        await db.SaveChangesAsync(cancellation);

        if (!await db.Responses.AnyAsync(cancellation))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
            foreach (var claim in Read("seed/claims.json").AsArray())
            {
                var response = Response("claim", claim!["data"]!, (int)claim["daysAgo"]!, (string?)claim["definitionVersion"]);
                db.Responses.Add(response);
                await db.SaveChangesAsync(cancellation);
                db.Claims.Add(new StoredClaim
                {
                    ResponseId = response.Id,
                    CustomerEmail = (string?)claim["data"]!["customer_email"],
                    Amount = (decimal?)claim["data"]!["amount"],
                });
            }
            foreach (var sheet in Read("seed/job-sheets.json").AsArray())
            {
                db.Responses.Add(Response("job-sheet", sheet!["data"]!, (int)sheet["daysAgo"]!));
            }
            foreach (var row in Read("seed/feedback-responses.json").AsArray())
            {
                db.Responses.Add(Response("feedback", row!["data"]!, (int)row["daysAgo"]!));
            }
            await db.SaveChangesAsync(cancellation);
            await transaction.CommitAsync(cancellation);
        }

        // load-definition-variables-data: one claim request per demo user, matched on the stored text
        foreach (var request in Read("seed/claim-requests.json").AsArray())
        {
            var data = StoredJson.Encode(request);
            if (!await db.Responses.AnyAsync(r => r.FormId == "claim" && r.Data == data, cancellation))
            {
                db.Responses.Add(new StoredResponse { FormId = "claim", Data = data, CreatedAt = StoredJson.Now() });
                await db.SaveChangesAsync(cancellation);
            }
        }
    }

    /// <summary>A seeded response, dated daysAgo days back at a time of day that varies with it.</summary>
    private static StoredResponse Response(string formId, JsonNode data, int daysAgo, string? version = null)
    {
        var day = DateTime.UtcNow.Date.AddDays(-daysAgo);
        var createdAt = day.AddHours(9 + daysAgo % 9).AddMinutes(daysAgo * 7 % 60);
        return new StoredResponse
        {
            FormId = formId,
            DefinitionVersion = version,
            Data = StoredJson.Encode(data),
            CreatedAt = StoredJson.Iso(createdAt),
        };
    }

    private JsonNode Read(string path) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(sharedRoot, path)))
        ?? throw new InvalidDataException($"shared/{path} is empty");
}
