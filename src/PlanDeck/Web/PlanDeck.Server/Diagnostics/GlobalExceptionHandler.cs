using System.Diagnostics;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PlanDeck.Server.Diagnostics;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    private const string GenericMessage =
        "An unexpected error occurred. Contact support with the trace ID.";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        logger.LogError(
            exception,
            "Unhandled server exception. Trace ID: {TraceId}",
            traceId);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        if (IsBrowserNavigation(httpContext.Request))
        {
            httpContext.Response.ContentType = "text/html; charset=utf-8";
            var encodedTraceId = HtmlEncoder.Default.Encode(traceId);
            await httpContext.Response.WriteAsync(
                $"""
                <!doctype html>
                <html lang="en">
                <head><meta charset="utf-8"><title>Server error</title></head>
                <body>
                  <h1>Server error</h1>
                  <p>{GenericMessage}</p>
                  <p>Trace ID: <code>{encodedTraceId}</code></p>
                </body>
                </html>
                """,
                cancellationToken);
            return true;
        }

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Server error",
                Detail = GenericMessage,
                Extensions = { ["traceId"] = traceId }
            },
            Exception = exception
        });
        return true;
    }

    private static bool IsBrowserNavigation(HttpRequest request)
    {
        return string.Equals(
                request.Headers["Sec-Fetch-Mode"].ToString(),
                "navigate",
                StringComparison.OrdinalIgnoreCase)
            && request.GetTypedHeaders().Accept?.Any(
                mediaType => mediaType.MediaType.HasValue
                    && string.Equals(
                        mediaType.MediaType.Value,
                        "text/html",
                        StringComparison.OrdinalIgnoreCase)) == true;
    }
}
