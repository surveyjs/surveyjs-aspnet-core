using System.Threading.RateLimiting;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Demo;

/// <summary>
/// Demo only: /api/translate and /api/work-orders/extract allow 20 calls per visitor sandbox per day (DEMO_MODE=true),
/// with ASP.NET Core's rate limiter. Outside demo mode there is no sandbox id, so nothing is limited.
/// </summary>
public static class DemoAiLimit
{
    private static readonly PathString[] AiPaths = ["/api/translate", "/api/work-orders/extract"];

    public static IServiceCollection AddDemoAiLimit(this IServiceCollection services, int callsPerDay) =>
        services.AddRateLimiter(limiter =>
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var sandbox = context.RequestServices.GetRequiredService<RequestStorage>().SandboxId;
                return sandbox is not null && HttpMethods.IsPost(context.Request.Method) && AiPaths.Contains(context.Request.Path)
                    ? RateLimitPartition.GetFixedWindowLimiter(sandbox, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = callsPerDay, Window = TimeSpan.FromDays(1), QueueLimit = 0,
                    })
                    : RateLimitPartition.GetNoLimiter("");
            });
            limiter.OnRejected = (rejected, _) => new ValueTask(ApiErrors.WriteAsync(
                rejected.HttpContext, StatusCodes.Status429TooManyRequests, $"The demo allows {callsPerDay} AI calls per visitor per day"));
        });
}
