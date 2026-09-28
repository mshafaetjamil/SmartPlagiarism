using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class UploadedFileConfiguration : IEntityTypeConfiguration<UploadedFile>
{
    public void Configure(EntityTypeBuilder<UploadedFile> builder)
    {
        builder.HasKey(file => file.Id);

        builder.Property(file => file.OriginalFileName)
            .IsRequired()
            .HasMaxLength(260);

        builder.Property(file => file.ContentType)
            .IsRequired()
            .HasMaxLength(128);

        // Hex SHA-256 is always 64 ASCII characters: char(64) rather than
        // nvarchar(64) halves the storage and the index.
        builder.Property(file => file.Sha256Hash)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength()
            .IsUnicode(false);

        builder.Property(file => file.StoragePath)
            .IsRequired()
            .HasMaxLength(400);

        // Non-unique by design: the same bytes appearing twice is the duplicate
        // we want to find, and this index is what makes that lookup instant.
        builder.HasIndex(file => file.Sha256Hash);

        builder.HasOne(file => file.Submission)
            .WithMany(submission => submission.Files)
            .HasForeignKey(file => file.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        // One file per archive document, deleted with it.
        builder.HasOne(file => file.ArchiveDocument)
            .WithOne(document => document.File)
            .HasForeignKey<UploadedFile>(file => file.ArchiveDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // A file belongs to a submission or to the archive - never both, never
        // neither. Enforced by the database, since everything downstream
        // (reports, the match viewer) relies on it.
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_UploadedFiles_OneOwner",
            "([SubmissionId] IS NULL AND [ArchiveDocumentId] IS NOT NULL) OR ([SubmissionId] IS NOT NULL AND [ArchiveDocumentId] IS NULL)"));
    }
}
