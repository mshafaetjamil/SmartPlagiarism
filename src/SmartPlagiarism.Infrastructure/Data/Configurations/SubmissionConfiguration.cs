using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.HasKey(submission => submission.Id);

        builder.Property(submission => submission.Title)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(submission => submission.TeacherComment)
            .HasMaxLength(1000);

        // AnalysisStatus and Status have private setters so that the domain methods
        // on Submission are the only way to change them; EF writes through the
        // backing fields regardless.
        builder.Property(submission => submission.AnalysisStatus).IsRequired();
        builder.Property(submission => submission.Status).IsRequired();

        // The background worker polls for the oldest queued submission, so this
        // index is on the hot path of the analysis pipeline.
        builder.HasIndex(submission => submission.AnalysisStatus);

        // Teacher dashboards list "pending review for my course this semester".
        builder.HasIndex(submission => new { submission.CourseId, submission.SemesterId, submission.Status });

        // Student dashboard lists a student's own submissions, newest first.
        builder.HasIndex(submission => new { submission.StudentProfileId, submission.SubmittedAtUtc });

        // Restrict throughout: a course, semester or student with submissions
        // against it must not be deletable out from under the audit trail.
        builder.HasOne(submission => submission.StudentProfile)
            .WithMany(profile => profile.Submissions)
            .HasForeignKey(submission => submission.StudentProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(submission => submission.Course)
            .WithMany()
            .HasForeignKey(submission => submission.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(submission => submission.Semester)
            .WithMany()
            .HasForeignKey(submission => submission.SemesterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
