using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class PlagiarismResultConfiguration : IEntityTypeConfiguration<PlagiarismResult>
{
    public void Configure(EntityTypeBuilder<PlagiarismResult> builder)
    {
        builder.HasKey(result => result.Id);

        // decimal(5,2) holds 0.00-100.00 exactly; float would make two equal
        // scores compare unequal in reports.
        builder.Property(result => result.OverallSimilarityPercent)
            .HasPrecision(5, 2);

        builder.Property(result => result.EngineVersion)
            .IsRequired()
            .HasMaxLength(32);

        // Admin statistics aggregate over recent analyses.
        builder.HasIndex(result => result.AnalyzedAtUtc);

        // One current result per submission; a re-run replaces it.
        builder.HasOne(result => result.Submission)
            .WithOne(submission => submission.PlagiarismResult)
            .HasForeignKey<PlagiarismResult>(result => result.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
