using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SurveyJsAspNetCore.Data;

// One class per table, with the table and column names the docs use. A SurveyJS definition, response, draft or preset
// is one JSON document: those columns are strings holding the text the app wrote, never classes mapped to JSON,
// because a typed mapping drops keys the class doesn't have and rewrites numbers.

/// <summary>A form definition (Survey Creator saves it, the Form Library runs it).</summary>
[Table("forms")]
public class Form
{
    [Key, Column("key")] public string Key { get; set; } = "";
    [Column("json")] public string Json { get; set; } = "";
}

/// <summary>A submitted response: the answers as one JSON document.</summary>
[Table("responses")]
[Index(nameof(FormId)), Index(nameof(CreatedAt))]
public class StoredResponse
{
    [Column("id")] public long Id { get; set; }
    [Column("form_id")] public string FormId { get; set; } = "";
    [Column("definition_version")] public string? DefinitionVersion { get; set; }
    [Column("data")] public string Data { get; set; } = "";
    /// <summary>ISO 8601 UTC ("2026-11-17T09:30:00Z"), so date filters are string comparisons.</summary>
    [Column("created_at")] public string CreatedAt { get; set; } = "";
}

/// <summary>A saved draft (answers and UI state), keyed "&lt;user id&gt;:&lt;formId&gt;".</summary>
[Table("progress")]
public class ProgressEntry
{
    [Key, Column("key")] public string Key { get; set; } = "";
    [Column("json")] public string Json { get; set; } = "";
}

/// <summary>An uploaded file: the bytes are on disk, named by <see cref="Id"/>, never by the client's file name.</summary>
[Table("files")]
public class StoredFile
{
    [Key, Column("id")] public string Id { get; set; } = "";
    [Column("name")] public string Name { get; set; } = "";
    [Column("type")] public string Type { get; set; } = "";
}

[Table("offices")]
[Index(nameof(Region))]
public class Office
{
    [Key, Column("id")] public string Id { get; set; } = "";
    [Column("name")] public string Name { get; set; } = "";
    [Column("region")] public string Region { get; set; } = "";
}

[Table("customers")]
public class Customer
{
    [Key, Column("email")] public string Email { get; set; } = "";
}

[Table("shipping_rates")]
public class ShippingRate
{
    [Key, Column("postcode_prefix")] public string PostcodePrefix { get; set; } = "";
    [Column("price")] public decimal Price { get; set; }
}

/// <summary>Answers copied into columns for SQL (relational-storage); the original stays in responses.data.</summary>
[Table("claims")]
public class StoredClaim
{
    [Column("id")] public long Id { get; set; }
    [Column("response_id")] public long ResponseId { get; set; }
    [Column("customer_email")] public string? CustomerEmail { get; set; }
    [Column("amount")] public decimal? Amount { get; set; }
}

/// <summary>The Preview tab's variable presets for a form: one JSON array.</summary>
[Table("variable_presets")]
public class VariablePreset
{
    [Key, Column("key")] public string Key { get; set; } = "";
    [Column("json")] public string Json { get; set; } = "";
}

/// <summary>Demo only: the demo users (Demo/DemoUsers.cs). A real app has its own users, or ASP.NET Core Identity.</summary>
[Table("users")]
[Index(nameof(Key), IsUnique = true)]
public class User
{
    [Column("id")] public long Id { get; set; }
    [Column("key")] public string Key { get; set; } = "";
    [Column("name")] public string Name { get; set; } = "";
    [Column("email")] public string Email { get; set; } = "";
    /// <summary>"basic" or "premium": the forms read it as {user.plan}.</summary>
    [Column("plan")] public string Plan { get; set; } = "basic";
    /// <summary>Editors may save definitions and variable presets (the edit-forms policy).</summary>
    [Column("is_editor")] public bool IsEditor { get; set; }
}
