using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SurveyJsAspNetCore.Data;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>Async Calculations and Validation: the lookups behind emailExists() and shippingCost().</summary>
public static partial class AsyncFunctions
{
    public static IEndpointRouteBuilder MapAsyncFunctions(this IEndpointRouteBuilder app)
    {
        #region sjs:async-functions.server
        // GET /api/customers/exists?email= — answers emailExists()
        app.MapGet("/api/customers/exists", async (string? email, SurveyDb db) =>
        {
            var normalized = (email ?? "").Trim().ToLowerInvariant();
            return new { exists = await db.Customers.AnyAsync(c => c.Email == normalized) };
        });

        // GET /api/shipping?postcode= — answers shippingCost(): the longest matching postcode prefix wins,
        // { "price": null } when none matches (the form shows "We don't deliver to this postcode yet")
        app.MapGet("/api/shipping", async (string? postcode, SurveyDb db) =>
        {
            var code = (postcode ?? "").Replace(" ", "").ToUpperInvariant();
            // only a whole German (10115) or UK (SW1A 1AA) postcode gets a price, not anything that starts with a prefix
            if (!WholePostcode().IsMatch(code)) return new { price = (decimal?)null };
            var rates = await db.ShippingRates.ToListAsync();
            var rate = rates.Where(r => code.StartsWith(r.PostcodePrefix, StringComparison.Ordinal)).MaxBy(r => r.PostcodePrefix.Length);
            return new { price = rate?.Price };
        });
        #endregion
        return app;
    }

    [GeneratedRegex(@"^(\d{5}|[A-Z]{1,2}\d[A-Z\d]?\d[A-Z]{2})$")]
    private static partial Regex WholePostcode();
}
