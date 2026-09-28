using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Files;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Engine.Extraction;
using SmartPlagiarism.Engine.Ocr;
using SmartPlagiarism.Engine.Similarity;
using SmartPlagiarism.Infrastructure.Analysis;
using SmartPlagiarism.Infrastructure.Archive;
using SmartPlagiarism.Infrastructure.Data;
using SmartPlagiarism.Infrastructure.Services;
using SmartPlagiarism.Infrastructure.Similarity;
using SmartPlagiarism.Infrastructure.Storage;

namespace SmartPlagiarism.Tests.Analysis;

/// <summary>
/// The real analysis stack - file storage on disk, extraction, the EF fingerprint
/// index and corpus reader, the analyzer - over an in-memory SQLite database.
///
/// SQLite rather than EF's in-memory provider because the analyzer relies on
/// real relational behaviour: transactions, the unique 1:1 result, the SQL the
/// fingerprint index sends. One open connection keeps the database alive for the
/// test; each <see cref="NewContext"/> is a fresh unit of work on it, as each DI
/// scope is in the app.
/// </summary>
internal sealed class AnalysisTestHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _storageRoot;

    private AnalysisTestHarness(SqliteConnection connection, string storageRoot)
    {
        _connection = connection;
        _storageRoot = storageRoot;

        Storage = new LocalFileStorageService(
            Options.Create(new FileStorageOptions { RootPath = storageRoot }),
            new FileUploadValidator(),
            NullLogger<LocalFileStorageService>.Instance);
    }

    public LocalFileStorageService Storage { get; }

    public RecordingAnalysisQueue Queue { get; } = new();

    public string StudentUserId { get; private set; } = string.Empty;

    public string OtherStudentUserId { get; private set; } = string.Empty;

    private int CourseId { get; set; }

    private int SemesterId { get; set; }

    private int StudentProfileId { get; set; }

    private int OtherStudentProfileId { get; set; }

    public static async Task<AnalysisTestHarness> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var harness = new AnalysisTestHarness(
            connection,
            Path.Combine(Path.GetTempPath(), "smartplagiarism-analysis-tests", Guid.NewGuid().ToString("N")));

        await using var context = harness.NewContext();
        await context.Database.EnsureCreatedAsync();
        await harness.SeedAsync(context);

        return harness;
    }

    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    /// <summary>Points a DI-registered DbContext at this harness's database.</summary>
    public void UseDatabase(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

    public SubmissionAnalyzer NewAnalyzer(AppDbContext context, IExtractionService? extraction = null)
    {
        var pipeline = NewPipeline(context);
        return new(context, NewIndexer(context, extraction, pipeline), pipeline, NullLogger<SubmissionAnalyzer>.Instance);
    }

    /// <summary>Extraction and fingerprinting, as the analyzer and the archive importer use them.</summary>
    public DocumentIndexer NewIndexer(AppDbContext context, IExtractionService? extraction = null, SimilarityPipeline? pipeline = null) =>
        new(
            context,
            extraction ?? new ExtractionService(
                context,
                Storage,
                ExtractorFactory.CreateDefault(new NullOcrService("no OCR in tests")),
                NullLogger<ExtractionService>.Instance),
            pipeline ?? NewPipeline(context));

    private static SimilarityPipeline NewPipeline(AppDbContext context) =>
        new(
            new EfFingerprintIndex(context),
            new EfCorpusReader(context),
            CombinedSimilarity.CreateDefault(UniformInverseDocumentFrequency.Instance));

    public ArchiveImporter NewImporter(AppDbContext context, IExtractionService? extraction = null) =>
        new(context, Storage, new FileUploadValidator(), NewIndexer(context, extraction), NullLogger<ArchiveImporter>.Instance);

    public ArchiveService NewArchiveService(AppDbContext context) =>
        new(context, Storage, Queue, new AuditService(context, NullLogger<AuditService>.Instance));

    /// <summary>Queues a ZIP as an admin would and runs its import, as the worker does.</summary>
    /// <returns>The import's id.</returns>
    public async Task<int> ImportArchiveAsync(byte[] zip, int? defaultYear = null, int? defaultDepartmentId = null)
    {
        await using (var context = NewContext())
        await using (var content = new MemoryStream(zip))
        {
            var result = await NewArchiveService(context).QueueImportAsync(
                new Core.DTOs.Archive.ArchivePackage("archive.zip", zip.Length, content, defaultYear, defaultDepartmentId),
                "admin-id");

            result.Succeeded.Should().BeTrue(string.Join(" ", result.Errors));
        }

        var importId = Queue.Enqueued[^1];

        await using (var context = NewContext())
        {
            await NewImporter(context).ImportAsync(importId);
        }

        return importId;
    }

    public async Task<int> DepartmentIdAsync(string code)
    {
        await using var context = NewContext();
        return await context.Departments.Where(department => department.Code == code).Select(department => department.Id).SingleAsync();
    }

    /// <summary>Analyses a submission in its own unit of work, as the worker does.</summary>
    public async Task AnalyzeAsync(int submissionId)
    {
        await using var context = NewContext();
        await NewAnalyzer(context).AnalyzeAsync(submissionId);
    }

    /// <summary>Stores the files and creates a queued submission holding them.</summary>
    public async Task<int> SubmitAsync(params (string FileName, byte[] Content)[] files) =>
        await SubmitAsAsync(StudentProfileId, files);

    public async Task<int> SubmitAsOtherStudentAsync(params (string FileName, byte[] Content)[] files) =>
        await SubmitAsAsync(OtherStudentProfileId, files);

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();

        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private async Task<int> SubmitAsAsync(int studentProfileId, (string FileName, byte[] Content)[] files)
    {
        var submission = new Submission
        {
            StudentProfileId = studentProfileId,
            CourseId = CourseId,
            SemesterId = SemesterId,
            Title = "Test submission",
            Type = Core.Enums.SubmissionType.Report,
            SubmittedAtUtc = DateTime.UtcNow,
        };

        foreach (var (fileName, content) in files)
        {
            await using var stream = new MemoryStream(content);
            var stored = await Storage.StoreAsync(new FileUpload(fileName, content.Length, stream));

            submission.Files.Add(new UploadedFile
            {
                OriginalFileName = fileName,
                ContentType = stored.ContentType,
                SizeBytes = stored.SizeBytes,
                Sha256Hash = stored.Sha256Hash,
                StoragePath = stored.StoragePath,
            });
        }

        await using var context = NewContext();
        context.Submissions.Add(submission);
        await context.SaveChangesAsync();

        return submission.Id;
    }

    private async Task SeedAsync(AppDbContext context)
    {
        var department = new Department { Code = "CSE", Name = "Computer Science and Engineering" };
        var course = new Course { Department = department, Code = "CSE-400", Title = "Project" };
        var semester = new Semester
        {
            Name = "Fall 2026",
            StartDateUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
        };

        var student = NewUser("student@uni.edu");
        var otherStudent = NewUser("other@uni.edu");

        var profile = new StudentProfile { User = student, StudentNumber = "2026-001", Department = department, EnrollmentYear = 2026 };
        var otherProfile = new StudentProfile { User = otherStudent, StudentNumber = "2026-002", Department = department, EnrollmentYear = 2026 };

        context.AddRange(course, semester, profile, otherProfile);
        await context.SaveChangesAsync();

        CourseId = course.Id;
        SemesterId = semester.Id;
        StudentProfileId = profile.Id;
        OtherStudentProfileId = otherProfile.Id;
        StudentUserId = student.Id;
        OtherStudentUserId = otherStudent.Id;
    }

    private static ApplicationUser NewUser(string email) => new()
    {
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FullName = email,
    };
}

/// <summary>A queue - of submissions or archive imports - that only records what was enqueued.</summary>
internal sealed class RecordingAnalysisQueue : IAnalysisQueue, IArchiveImportQueue
{
    public List<int> Enqueued { get; } = [];

    public ValueTask EnqueueAsync(int id, CancellationToken cancellationToken = default)
    {
        Enqueued.Add(id);
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<int> DequeueAllAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("Tests read Enqueued directly.");
}
