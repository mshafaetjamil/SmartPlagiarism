using System.Diagnostics;

namespace SmartPlagiarism.Web.ErrorHandling;

/// <summary>
/// What a user sees when something goes wrong, in one place.
///
/// <list type="bullet">
///   <item><description>
///     An unhandled exception: logged by <see cref="GlobalExceptionHandler"/>,
///     then a friendly page (or problem-details JSON for /api) with the trace id
///     to quote. In Development the developer exception page shows the details
///     instead.
///   </description></item>
///   <item><description>
///     An error status with no body - a 404 for another student's submission, a
///     400 for an expired form: the same friendly page for that status, or
///     problem-details JSON for /api.
///   </description></item>
/// </list>
/// </summary>
public static class ErrorHandlingExtensions
{
    /// <summary>The friendly error page; /Error/{status} for a status with no body.</summary>
    public const string ErrorPath = "/Error";

    private const string ApiPathPrefix = "/api";

    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        // Every problem-details response - errors here, and the API's own 400s
        // and 404s - carries the trace id the logs record, so a client's report
        // can be matched to the log entry.
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = TraceId(context.HttpContext));

        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }

    /// <summary>Goes first in the pipeline, so everything after it is covered.</summary>
    public static IApplicationBuilder UseErrorHandling(this IApplicationBuilder app, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            app.UseExceptionHandler(new ExceptionHandlerOptions
            {
                ExceptionHandlingPath = ErrorPath,

                // An oversized or malformed request is the client's error, so it
                // gets its own status - 413, 400 - rather than a 500.
                StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
                    ? badRequest.StatusCode
                    : StatusCodes.Status500InternalServerError,

                // GlobalExceptionHandler has already logged it, with more context.
                SuppressDiagnosticsCallback = _ => true,
            });
        }

        // The API answers in JSON - problem details, from AddProblemDetails - and
        // the site with its error page. A response that already has a body, such
        // as the API's own validation errors, is left alone.
        app.UseWhen(context => IsApiPath(context.Request.Path), api => api.UseStatusCodePages());
        app.UseWhen(
            context => !IsApiPath(context.Request.Path),
            site => site.UseStatusCodePagesWithReExecute(ErrorPath + "/{0}"));

        return app;
    }

    /// <summary>The id quoted on error pages and in problem details, and recorded with every log entry.</summary>
    public static string TraceId(HttpContext context) =>
        Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

    internal static bool IsApiPath(PathString path) => path.StartsWithSegments(ApiPathPrefix, StringComparison.OrdinalIgnoreCase);
}
