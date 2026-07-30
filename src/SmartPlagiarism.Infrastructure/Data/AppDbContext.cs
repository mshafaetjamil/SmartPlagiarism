using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Identity;

namespace SmartPlagiarism.Infrastructure.Data;

/// <summary>
/// The application database context: ASP.NET Core Identity plus the domain model.
/// Lazy loading is deliberately left off (EF Core's default) - read queries use
/// explicit Include and AsNoTracking.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Course> Courses => Set<Course>();

    public DbSet<Semester> Semesters => Set<Semester>();

    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();

    public DbSet<TeacherProfile> TeacherProfiles => Set<TeacherProfile>();

    public DbSet<Submission> Submissions => Set<Submission>();

    public DbSet<UploadedFile> UploadedFiles => Set<UploadedFile>();

    public DbSet<ExtractedText> ExtractedTexts => Set<ExtractedText>();

    public DbSet<DocumentFingerprint> DocumentFingerprints => Set<DocumentFingerprint>();

    public DbSet<PlagiarismResult> PlagiarismResults => Set<PlagiarismResult>();

    public DbSet<MatchedDocument> MatchedDocuments => Set<MatchedDocument>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Picks up every IEntityTypeConfiguration<T> in this assembly, so new
        // entity configurations are wired up simply by existing.
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
