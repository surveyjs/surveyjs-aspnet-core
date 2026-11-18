using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SurveyJsAspNetCore.Data;

namespace SurveyJsAspNetCore.Demo;

/// <summary>
/// Demo only: a demo_user cookie picks Alice (premium, editor), Bob (basic, viewer) or Signed out ("none"). Pages that
/// demonstrate user context or authorization offer a switch (Pages); every other page signs in as Alice, so a choice made
/// on one page never breaks another. The examples only read HttpContext.User and call IAuthorizationService.
/// </summary>
public static class DemoUsers
{
    public const string Scheme = "Demo";
    public const string Cookie = "demo_user";
    public const string KeyClaim = "demo_user";
    public const string PlanClaim = "plan";
    public const string EditorClaim = "is_editor";

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["alice"] = "Alice (premium, editor)",
        ["bob"] = "Bob (basic, viewer)",
        ["none"] = "Signed out",
    };

    /// <summary>Step slug => the users its switch offers. The first is the default.</summary>
    private static readonly Dictionary<string, string[]> Pages = new()
    {
        ["load-definition-variables-data"] = ["alice", "bob"],   // compare premium and basic customer variables
        ["resume-progress"] = ["alice", "bob"],                  // an independent draft for each respondent
        ["creator-load-save"] = ["alice", "bob"],                // Bob's saves answer 403 (the edit-forms policy)
    };

    /// <summary>The users a step page offers; one means no switch.</summary>
    public static string[] ForPage(string slug) => Pages.GetValueOrDefault(slug) ?? ["alice"];

    /// <summary>The current demo user's key: alice, bob or none.</summary>
    public static string Key(ClaimsPrincipal user) => user.FindFirstValue(KeyClaim) ?? "none";

    /// <summary>On a step page: keep the cookie's user if the page offers it, otherwise sign in as the page's default.</summary>
    public static async Task SignInForPageAsync(string slug, HttpContext context)
    {
        var users = ForPage(slug);
        if (users.Contains(context.Request.Cookies[Cookie]))
        {
            return;
        }
        // host.js reads and sets this cookie, so it isn't HttpOnly
        context.Response.Cookies.Append(Cookie, users[0], new CookieOptions
        {
            Path = "/", HttpOnly = false, SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps, MaxAge = TimeSpan.FromDays(365),
        });
        var db = context.RequestServices.GetRequiredService<SurveyDb>();
        context.User = await PrincipalAsync(db, users[0]) ?? new ClaimsPrincipal(new ClaimsIdentity());
    }

    /// <summary>The demo user with this key as a ClaimsPrincipal, or null for none or an unknown key.</summary>
    public static async Task<ClaimsPrincipal?> PrincipalAsync(SurveyDb db, string? key)
    {
        if (key is null or "none")
        {
            return null;
        }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Key == key);
        if (user is null)
        {
            return null;
        }
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(PlanClaim, user.Plan),
            new Claim(EditorClaim, user.IsEditor ? "true" : "false"),
            new Claim(KeyClaim, user.Key),
        ], Scheme);
        return new ClaimsPrincipal(identity);
    }
}

/// <summary>
/// Demo only: makes the demo_user cookie's user the request's user (HttpContext.User), everywhere, the WebSocket
/// endpoints included. Real apps authenticate here with cookie or JWT authentication (AddCookie, AddJwtBearer).
/// </summary>
public sealed class DemoAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var db = Context.RequestServices.GetRequiredService<SurveyDb>();
        var user = await DemoUsers.PrincipalAsync(db, Request.Cookies[DemoUsers.Cookie]);
        return user is null ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new AuthenticationTicket(user, Scheme.Name));
    }
}
