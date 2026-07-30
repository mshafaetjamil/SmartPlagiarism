using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Files;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Engine.Extraction;
using SmartPlagiarism.Engine.Ocr;
using SmartPlagiarism.Infrastructure.Data;
using SmartPlagiarism.Infrastructure.Services;
using SmartPlagiarism.Infrastructure.Storage;

namespace SmartPlagiarism.Infrastructure;

/// <summary>
/// Single composition point for everything Infrastructure owns, so the Web
/// project's Program.cs never has to know about EF Core or Identity stores.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' was not found. Add it to appsettings.Development.json or user secrets.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Accounts are created by an administrator, not by public signup,
                // so there is no confirmation step to gate sign-in on.
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // Relative paths are resolved against the content root here, once, so the
        // storage service only ever deals with an absolute path.
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, environment) =>
                options.RootPath = Path.GetFullPath(options.RootPath, environment.ContentRootPath))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IFileUploadValidator, FileUploadValidator>();
        services.AddSingleton<IFileStorageService, LocalFileStorageService>();

        AddTextExtraction(services, configuration);

        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ISubmissionService, SubmissionService>();
        services.AddScoped<IExtractionService, ExtractionService>();

        return services;
    }

    /// <summary>
    /// Wires up the Engine. It has no dependency-injection package of its own - by
    /// design, since CLAUDE.md keeps it to the BCL and its parsing libraries - so
    /// its types are registered here.
    /// </summary>
    private static void AddTextExtraction(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OcrOptions>()
            .Bind(configuration.GetSection("Ocr"))
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                if (!string.IsNullOrWhiteSpace(options.TessDataPath))
                {
                    options.TessDataPath = Path.GetFullPath(options.TessDataPath, environment.ContentRootPath);
                }
            });

        // Singleton: TesseractOcrService probes for the native library once and
        // holds a single engine instance, serialising access internally.
        services.AddSingleton<IOcrService>(provider =>
            new TesseractOcrService(provider.GetRequiredService<IOptions<OcrOptions>>().Value));

        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextExtractor, DocxTextExtractor>();
        services.AddSingleton<ITextExtractor, PptxTextExtractor>();

        services.AddSingleton<IExtractorFactory>(provider =>
            new ExtractorFactory(provider.GetServices<ITextExtractor>()));
    }
}
