using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace SmartPlagiarism.Web.Extensions;

/// <summary>Where the log file goes. An empty path turns the file off.</summary>
public sealed class LogFileOptions
{
    public const string SectionName = "LogFile";

    /// <summary>
    /// Relative to the content root, like the upload store. The date is added
    /// before the extension, giving one file per day.
    /// </summary>
    public string? Path { get; set; } = "App_Data/logs/smartplagiarism-.json";

    /// <summary>Days of files kept; older ones are deleted as new ones start.</summary>
    [Range(1, 3650)]
    public int RetainedFileCount { get; set; } = 30;
}

/// <summary>
/// Structured logging with Serilog: every event keeps its named properties -
/// submission id, user id, trace id - rather than being flattened into text.
///
/// Two sinks: the console, readable while developing, and a rolling file of
/// JSON events (one object per line), which can be searched by property and
/// loaded into a log viewer such as Seq. Levels and any further sinks come from
/// the "Serilog" section of appsettings, so they can change without a rebuild.
/// </summary>
public static class LoggingExtensions
{
    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>A single file never grows past this; a new one is started instead.</summary>
    private const long MaxFileBytes = 100L * 1024 * 1024;

    public static WebApplicationBuilder AddStructuredLogging(this WebApplicationBuilder builder)
    {
        var fileOptions = builder.Configuration.GetSection(LogFileOptions.SectionName).Get<LogFileOptions>() ?? new LogFileOptions();
        Validator.ValidateObject(fileOptions, new ValidationContext(fileOptions), validateAllProperties: true);

        var filePath = string.IsNullOrWhiteSpace(fileOptions.Path)
            ? null
            : Path.GetFullPath(fileOptions.Path, builder.Environment.ContentRootPath);

        // Serilog takes over from the default console and debug providers, which
        // would otherwise write every event a second time.
        builder.Logging.ClearProviders();

        builder.Services.AddSerilog(
            (services, logger) =>
            {
                logger
                    .ReadFrom.Configuration(builder.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(outputTemplate: ConsoleTemplate, formatProvider: CultureInfo.InvariantCulture);

                if (filePath is not null)
                {
                    logger.WriteTo.File(
                        new RenderedCompactJsonFormatter(),
                        filePath,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: fileOptions.RetainedFileCount,
                        fileSizeLimitBytes: MaxFileBytes,
                        rollOnFileSizeLimit: true);
                }
            },

            // Each app's loggers write through its own Serilog logger, never the
            // static Log.Logger - which, when several hosts share a process as
            // the integration tests do, belongs to whichever started last.
            preserveStaticLogger: true,

            // Providers added through ILoggingBuilder later on - an Application
            // Insights provider, the integration tests' log capture - still
            // receive every event.
            writeToProviders: true);

        return builder;
    }

    /// <summary>
    /// One event per request - method, path, status, time taken, user - in place
    /// of the framework's several. Goes first, so it times and records every
    /// request, including the ones that end on an error page.
    /// </summary>
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            // This app's logger: the middleware would otherwise use the static
            // Log.Logger, which AddStructuredLogging leaves alone.
            options.Logger = app.ApplicationServices.GetRequiredService<Serilog.ILogger>();

            options.GetLevel = (context, _, exception) =>
                exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? LogEventLevel.Error
                    : IsStatusPoll(context) ? LogEventLevel.Debug : LogEventLevel.Information;

            options.EnrichDiagnosticContext = (diagnostics, context) =>
            {
                if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId)
                {
                    diagnostics.Set("UserId", userId);
                }
            };
        });

    /// <summary>
    /// The analysis-status endpoints a page polls every few seconds while work is
    /// queued; at Information they would bury everything else.
    /// </summary>
    private static bool IsStatusPoll(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && (context.Request.Path.Equals("/Submissions/Status", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.Equals("/Analysis/Status", StringComparison.OrdinalIgnoreCase));
}
