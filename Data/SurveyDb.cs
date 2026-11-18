using Microsoft.EntityFrameworkCore;

namespace SurveyJsAspNetCore.Data;

/// <summary>
/// The app's tables, through EF Core on SQLite. JSON documents are string properties (see Entities.cs), so a stored
/// response stays one document. Queries don't track entities by default (SurveyDbSetup), so read code needs no
/// AsNoTracking(); writes add new entities or go through StoredJson.UpsertAsync.
/// </summary>
public class SurveyDb(DbContextOptions<SurveyDb> options) : DbContext(options)
{
    public DbSet<Form> Forms => Set<Form>();
    public DbSet<StoredResponse> Responses => Set<StoredResponse>();
    public DbSet<ProgressEntry> Progress => Set<ProgressEntry>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    public DbSet<Office> Offices => Set<Office>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ShippingRate> ShippingRates => Set<ShippingRate>();
    public DbSet<StoredClaim> Claims => Set<StoredClaim>();
    public DbSet<VariablePreset> VariablePresets => Set<VariablePreset>();
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// A field inside a stored JSON document, as SQLite's json_extract(json, path) reads it: usable in LINQ queries,
    /// for example <c>.Where(r =&gt; SurveyDb.JsonValue(r.Data, "$.customer_email") == email)</c>.
    /// </summary>
    public static string? JsonValue(string json, string path) =>
        throw new NotSupportedException("SurveyDb.JsonValue runs in SQL only, inside a LINQ query");

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDbFunction(() => JsonValue(default!, default!)).HasName("json_extract").IsBuiltIn();

        // claims.response_id references responses.id; no navigation properties, the code joins when it needs to
        model.Entity<StoredClaim>().HasOne<StoredResponse>().WithMany().HasForeignKey(c => c.ResponseId);
    }
}
