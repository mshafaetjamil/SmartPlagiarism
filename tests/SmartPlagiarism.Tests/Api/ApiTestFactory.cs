using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Tests.Api;

/// <summary>What the factory seeded: the ids a request needs, and two API keys.</summary>
public sealed record ApiTestData(int CourseId, int SemesterId, string StudentNumber, string PortalKey, string OtherKey, int RevokedKeyId, string RevokedKey);

/// <summary>
/// The real web app - its middleware, authentication, rate limiter, controllers,
/// services and background worker - hosted in memory. Only SQL Server is swapped
/// for SQLite, file storage for a temporary folder, and OCR is off.
///
/// The SQLite database is a temporary file in WAL mode, not in-memory: the
/// background worker writes while test requests read and write, on other
/// threads, and one shared in-memory connection is not safe for that. With a
/// file each DbContext opens its own connection and SQLite's locking arbitrates.
/// </summary>
public class ApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smartplagiarism-api-tests", Guid.NewGuid().ToString("N"));
    private readonly SemaphoreSlim _seedGate = new(1, 1);
    private ApiTestData? _data;

    /// <summary>
    /// Warnings and errors the app logged, so a test that fails because of
    /// something inside the app - a failed analysis - can say what it was.
    /// </summary>
    public CapturedLog Log { get; } = new();

    /// <summary>Uploads allowed per key per minute; high by default so only the rate-limit test meets it.</summary>
    protected virtual int UploadsPerMinute => 1000;

    /// <summary>Submissions one student may make a day; high by default so only the submission-limit test meets it.</summary>
    protected virtual int SubmissionsPerStudentPerDay => 1000;

    /// <summary>Where the app's JSON log files go - the temporary folder, not the source tree.</summary>
    public string LogDirectory => Path.Combine(_root, "logs");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_root);
        var database = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_root, "api-tests.db") }.ToString();

        // Not Development: no demo seeding, and no Swagger - as in production.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=replaced-by-sqlite-in-tests");
        builder.UseSetting("FileStorage:RootPath", Path.Combine(_root, "uploads"));
        builder.UseSetting("LogFile:Path", Path.Combine(LogDirectory, "app-.json"));
        builder.UseSetting("Ocr:Enabled", "false");
        builder.UseSetting("Api:UploadsPerMinute", UploadsPerMinute.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Submissions:MaxPerStudentPerDay", SubmissionsPerStudentPerDay.ToString(CultureInfo.InvariantCulture));

        builder.ConfigureLogging(logging => logging.AddProvider(Log));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(database));
        });
    }

    /// <summary>Creates the schema and the data every test needs, once per factory.</summary>
    public async Task<ApiTestData> SeedAsync()
    {
        await _seedGate.WaitAsync();

        try
        {
            return _data ??= await CreateDataAsync();
        }
        finally
        {
            _seedGate.Release();
        }
    }

    /// <summary>A client that sends <paramref name="apiKey"/> in the X-Api-Key header.</summary>
    public HttpClient ClientWithKey(string? apiKey)
    {
        var client = CreateClient();

        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        return client;
    }

    public async Task<int> CountSubmissionsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Submissions.CountAsync();
    }

    /// <summary>
    /// The first event in the JSON log file that <paramref name="match"/> accepts.
    /// Waits a little: a request's own event is written as it finishes, which can
    /// be just after the client has its response.
    /// </summary>
    public async Task<JsonElement> WaitForLogEventAsync(Func<JsonElement, bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            foreach (var file in Directory.Exists(LogDirectory) ? Directory.GetFiles(LogDirectory) : [])
            {
                if (await FindLogEventAsync(file, match) is { } found)
                {
                    return found;
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No matching event was logged to {LogDirectory}. Last events:\n{await LastLogLinesAsync(5)}");
            }

            await Task.Delay(100);
        }
    }

    private async Task<string> LastLogLinesAsync(int count)
    {
        var lines = new List<string>();

        foreach (var file in Directory.Exists(LogDirectory) ? Directory.GetFiles(LogDirectory) : [])
        {
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            lines.AddRange((await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }

        return lines.Count == 0 ? "(no log file)" : string.Join('\n', lines.TakeLast(count));
    }

    private static async Task<JsonElement?> FindLogEventAsync(string file, Func<JsonElement, bool> match)
    {
        // The app still has the file open for writing.
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            using var logEvent = JsonDocument.Parse(line);

            if (match(logEvent.RootElement))
            {
                return logEvent.RootElement.Clone();
            }
        }

        return null;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _seedGate.Dispose();

            // Pooled connections would keep the database file open.
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private async Task<ApiTestData> CreateDataAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var context = provider.GetRequiredService<AppDbContext>();

        await context.Database.EnsureCreatedAsync();

        // Write-ahead logging: readers never block the writer, and a second
        // writer waits its turn instead of failing with "database is locked" -
        // closer to how SQL Server behaves when the worker and a request write at
        // once. The setting is stored in the file, so it applies to every connection.
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

        var roles = provider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in ApplicationRoles.All)
        {
            await roles.CreateAsync(new IdentityRole(role));
        }

        var department = new Department { Code = "CSE", Name = "Computer Science and Engineering" };
        var course = new Course { Department = department, Code = "CSE402", Title = "Final Year Project" };
        var semester = new Semester
        {
            Name = "Fall 2026",
            StartDateUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
        };

        context.AddRange(course, semester);
        await context.SaveChangesAsync();

        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var student = new ApplicationUser
        {
            UserName = "portal.student@uni.edu",
            Email = "portal.student@uni.edu",
            FullName = "Portal Student",
            EmailConfirmed = true,
        };

        await users.CreateAsync(student, "Student#2026");
        await users.AddToRoleAsync(student, ApplicationRoles.Student);

        context.StudentProfiles.Add(new StudentProfile
        {
            UserId = student.Id,
            StudentNumber = "2026-API-001",
            DepartmentId = department.Id,
            EnrollmentYear = 2026,
        });
        await context.SaveChangesAsync();

        var apiKeys = provider.GetRequiredService<IApiKeyService>();
        var portal = (await apiKeys.CreateAsync("Student portal", "admin")).Key!;
        var other = (await apiKeys.CreateAsync("Another integration", "admin")).Key!;
        var revoked = (await apiKeys.CreateAsync("Old portal", "admin")).Key!;
        await apiKeys.RevokeAsync(revoked.Id, "admin");

        return new ApiTestData(course.Id, semester.Id, "2026-API-001", portal.Key, other.Key, revoked.Id, revoked.Key);
    }
}

/// <summary>The same app with a small upload allowance, for the rate-limit test.</summary>
public sealed class RateLimitedApiTestFactory : ApiTestFactory
{
    public const int Limit = 2;

    protected override int UploadsPerMinute => Limit;
}

/// <summary>The same app allowing each student one submission a day, for the submission-limit test.</summary>
public sealed class SubmissionLimitedApiTestFactory : ApiTestFactory
{
    protected override int SubmissionsPerStudentPerDay => 1;
}

/// <summary>An in-memory log of the app's warnings and errors, exceptions included.</summary>
public sealed class CapturedLog : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _entries = new();

    public override string ToString() => string.Join(Environment.NewLine, _entries);

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, System.Collections.Concurrent.ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
            }
        }
    }
}
