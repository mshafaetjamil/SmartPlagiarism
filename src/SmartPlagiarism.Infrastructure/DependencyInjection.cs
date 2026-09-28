using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Files;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Engine.Extraction;
using SmartPlagiarism.Engine.Fingerprinting;
using SmartPlagiarism.Engine.Ocr;
using SmartPlagiarism.Engine.Similarity;
using SmartPlagiarism.Infrastructure.Admin;
using SmartPlagiarism.Infrastructure.Analysis;
using SmartPlagiarism.Infrastructure.Archive;
using SmartPlagiarism.Infrastructure.Data;
using SmartPlagiarism.Infrastructure.Reports;
using SmartPlagiarism.Infrastructure.Services;
using SmartPlagiarism.Infrastructure.Similarity;
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

        // Explicit transactions here (re-indexing a document) are never retried
        // within: a failed save is rolled back whole when the transaction is
        // disposed, so the savepoints MARS disables are not needed, and warning
        // about them on every analysis would only bury real problems in the log.
        services.AddDbContext<AppDbContext>(options => options
            .UseSqlServer(connectionString)
            .ConfigureWarnings(warnings => warnings.Ignore(SqlServerEventId.SavepointsDisabledBecauseOfMARS)));

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Accounts are created by an administrator, not by public signup,
                // so there is no confirmation step to gate sign-in on.
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // Sign-in cookies are re-checked against the user's security stamp this
        // often. Deactivating an account changes the stamp, so this bounds how long
        // a deactivated user's open session survives (the default is 30 minutes).
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

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
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<ISubmissionAccessService, SubmissionAccessService>();

        services.AddOptions<ReportOptions>().Bind(configuration.GetSection(ReportOptions.SectionName));
        services.AddSingleton<IReportGenerator, QuestPdfReportGenerator>();
        services.AddScoped<IReportService, ReportService>();

        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IAcademicStructureService, AcademicStructureService>();
        services.AddScoped<IStatisticsService, StatisticsService>();
        services.AddScoped<IAuditLogReader, AuditLogReader>();
        services.AddScoped<IApiKeyService, ApiKeyService>();

        AddAnalysis(services, configuration);

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

        // Singleton: each service probes its platform once and serialises access
        // internally. The in-process library only loads on Windows; the
        // command-line program covers macOS and Linux.
        services.AddSingleton<IOcrService>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<OcrOptions>>().Value;

            return new FallbackOcrService([new TesseractOcrService(options), new TesseractCliOcrService(options)]);
        });

        services.AddOptions<ExtractionLimits>().Bind(configuration.GetSection("Extraction"));
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<ExtractionLimits>>().Value);

        services.AddSingleton<IPdfPageRenderer>(provider => new PdfiumPageRenderer(
            maxPixels: provider.GetRequiredService<ExtractionLimits>().MaxRenderedPagePixels));
        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextExtractor, DocxTextExtractor>();
        services.AddSingleton<ITextExtractor, PptxTextExtractor>();

        services.AddSingleton<IExtractorFactory>(provider =>
            new ExtractorFactory(provider.GetServices<ITextExtractor>()));

        AddSimilarity(services, configuration);
    }

    /// <summary>
    /// The background analysis pipeline. The queue is a singleton shared by the
    /// web requests that fill it and the worker that drains it; everything the
    /// worker runs is scoped and resolved per submission.
    /// </summary>
    private static void AddAnalysis(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnalysisOptions>(configuration.GetSection("Analysis"));
        services.AddSingleton<IAnalysisQueue, ChannelAnalysisQueue>();
        services.AddScoped<DocumentIndexer>();
        services.AddScoped<CommonTextFinder>();
        services.AddScoped<EngineUpgrade>();
        services.AddScoped<ISubmissionAnalyzer, SubmissionAnalyzer>();
        services.AddScoped<IAnalysisService, AnalysisService>();
        services.AddHostedService<AnalysisWorker>();

        // Archive imports index files the same way, on their own queue and worker.
        services.AddSingleton<IArchiveImportQueue, ChannelArchiveImportQueue>();
        services.AddScoped<IArchiveImporter, ArchiveImporter>();
        services.AddScoped<IArchiveService, ArchiveService>();
        services.AddHostedService<ArchiveImportWorker>();
    }

    /// <summary>
    /// The similarity engine and its two ports into storage, driven by
    /// <see cref="SubmissionAnalyzer"/>.
    /// </summary>
    private static void AddSimilarity(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SimilarityOptions>(configuration.GetSection("Similarity"));

        // Scoped: both read through the request's DbContext.
        services.AddScoped<IFingerprintIndex, EfFingerprintIndex>();
        services.AddScoped<ICorpusReader, EfCorpusReader>();

        // Corpus-wide IDF is not maintained yet, so every term is weighted equally.
        // Cosine then reduces to term frequency alone - weaker than a corpus-aware
        // weighting, but sound. Phase 8's statistics work is where real document
        // frequencies would come from.
        services.AddSingleton<IInverseDocumentFrequency>(UniformInverseDocumentFrequency.Instance);

        services.AddSingleton(provider =>
            CombinedSimilarity.CreateDefault(provider.GetRequiredService<IInverseDocumentFrequency>()));

        services.AddScoped(provider => new SimilarityPipeline(
            provider.GetRequiredService<IFingerprintIndex>(),
            provider.GetRequiredService<ICorpusReader>(),
            provider.GetRequiredService<CombinedSimilarity>(),
            provider.GetRequiredService<IOptions<SimilarityOptions>>().Value));
    }
}
