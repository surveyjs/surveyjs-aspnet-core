using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;

namespace SurveyJsAspNetCore.Integration;

/// <summary>Ends the request with this status and <c>{ error }</c>: thrown by the helpers (StoredJson, SurveyJsService).</summary>
public sealed class ErrorResponseException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>
/// Every error under /api is JSON <c>{ error }</c> with its status, the framework's own included: minimal APIs answer
/// a failed binding, an unknown route, a wrong method or a 415 with an empty body or application/problem+json, which the
/// SurveyJS clients can't show. The endpoints return their own errors with Results.Json(new { error }, statusCode: …).
/// </summary>
public static class ApiErrors
{
    public static bool IsApi(HttpContext context) => context.Request.Path.StartsWithSegments("/api");

    public static IServiceCollection AddApiErrors(this IServiceCollection services)
    {
        services.AddExceptionHandler<ApiExceptionHandler>();
        // Bad input (a body that isn't JSON, a query value that isn't a number) throws, so it reaches the handler below
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        return services;
    }

    public static WebApplication UseApiErrors(this WebApplication app)
    {
        // Exceptions the handler below doesn't take (outside /api): a plain 500
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = context => context.Response.WriteAsync("Internal server error"),
        });
        // A status with no body (404 for no route, 405, 415, 401/403 from the framework): write { error }
        app.UseStatusCodePages(context => IsApi(context.HttpContext)
            ? WriteAsync(context.HttpContext, context.HttpContext.Response.StatusCode, null)
            : Task.CompletedTask);
        return app;
    }

    public static Task WriteAsync(HttpContext context, int status, string? message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = message ?? ReasonPhrases.GetReasonPhrase(status) }, StoredJson.Options);
    }

    private sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> log) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            if (!IsApi(context) && exception is not ErrorResponseException)
            {
                return false;
            }
            var (status, message) = exception switch
            {
                ErrorResponseException e => (e.Status, e.Message),
                BadHttpRequestException { InnerException: JsonException } => (400, "The request body is not valid JSON"),
                BadHttpRequestException { StatusCode: 413 } => (413, "The request body is too large"),
                BadHttpRequestException e => (e.StatusCode, e.Message),
                _ => (500, "Internal server error"),
            };
            if (status >= 500 && exception is not ErrorResponseException)
            {
                log.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            }
            await WriteAsync(context, status, message);
            return true;
        }
    }
}
