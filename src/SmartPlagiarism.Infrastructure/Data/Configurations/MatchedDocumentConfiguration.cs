using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class MatchedDocumentConfiguration : IEntityTypeConfiguration<MatchedDocument>
{
    public void Configure(EntityTypeBuilder<MatchedDocument> builder)
    {
        builder.HasKey(match => match.Id);

        builder.Property(match => match.SimilarityPercent)
            .HasPrecision(5, 2);

        builder.Property(match => match.MatchedFragmentsJson)
            .IsRequired();

        builder.HasIndex(match => match.MatchedUploadedFileId);

        builder.HasOne(match => match.PlagiarismResult)
            .WithMany(result => result.Matches)
            .HasForeignKey(match => match.PlagiarismResultId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade: deleting a Submission already cascades to both its
        // UploadedFiles and its PlagiarismResult -> MatchedDocuments. Cascading here
        // too would give SQL Server two delete paths to the same rows, which it
        // rejects outright ("may cause cycles or multiple cascade paths").
        builder.HasOne(match => match.MatchedUploadedFile)
            .WithMany()
            .HasForeignKey(match => match.MatchedUploadedFileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
