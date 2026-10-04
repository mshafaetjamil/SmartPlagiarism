using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;

namespace SmartPlagiarism.Web.ErrorHandling;

/// <summary>
/// Every exception no code caught ends up here. It is logged once, with the
/// request it broke and who made it; then an API call gets problem-details
/// JSON, and anything else falls through to the friendly error page, which the
/// exception handler re-executes at /Error (see <see cref="ErrorHandlingExtensions"/>).
///
/// Nothing about the exception reaches the client - no message, no stack trace.
/// The page and the JSON carry the request's trace id instead, which finds the
/// full entry in the log.
/// </summary>
public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // By now the middleware has pointed the request at /Error; the feature
        // still knows the path that failed.
        var path = context.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? context.Request.Path.Value ?? string.Empty;
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (exception is BadHttpRequestException)
        {
            // The client's fault - a body over the size limit, say - not a bug.
            LogRejectedRequest(logger, exception, context.Request.Method, path, context.Response.StatusCode, userId);
        }
        else
        {
            LogUnhandledException(logger, exception, context.Request.Method, path, userId);
        }

        if (!ErrorHandlingExtensions.IsApiPath(path))
        {
            return false;
        }

        var status = context.Response.StatusCode;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = status,
                Title = status >= StatusCodes.Status500InternalServerError
                    ? "An unexpected error occurred."
                    : "The request could not be processed.",
                Detail = status >= StatusCodes.Status500InternalServerError
                    ? "The error has been logged. Quote the traceId when reporting it."
                    : null,
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception processing {RequestMethod} {RequestPath} for user {UserId}")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception, string requestMethod, string requestPath, string? userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected {RequestMethod} {RequestPath} with {StatusCode} for user {UserId}")]
    private static partial void LogRejectedRequest(ILogger logger, Exception exception, string requestMethod, string requestPath, int statusCode, string? userId);
}
