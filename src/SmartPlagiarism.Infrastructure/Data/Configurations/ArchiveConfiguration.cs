using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class ArchiveDocumentConfiguration : IEntityTypeConfiguration<ArchiveDocument>
{
    public void Configure(EntityTypeBuilder<ArchiveDocument> builder)
    {
        builder.HasKey(document => document.Id);

        builder.Property(document => document.Title)
            .IsRequired()
            .HasMaxLength(ArchiveDocument.MaxTitleLength);

        builder.Property(document => document.Author)
            .HasMaxLength(ArchiveDocument.MaxAuthorLength);

        builder.Property(document => document.CourseCode)
            .HasMaxLength(Course.MaxCodeLength);

        // The archive page lists and counts by year.
        builder.HasIndex(document => document.Year);

        builder.HasOne(document => document.Department)
            .WithMany()
            .HasForeignKey(document => document.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Documents outlive the import record that brought them in.
        builder.HasOne(document => document.ArchiveImport)
            .WithMany()
            .HasForeignKey(document => document.ArchiveImportId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class ArchiveImportConfiguration : IEntityTypeConfiguration<ArchiveImport>
{
    public void Configure(EntityTypeBuilder<ArchiveImport> builder)
    {
        builder.HasKey(import => import.Id);

        builder.Property(import => import.PackageFileName)
            .IsRequired()
            .HasMaxLength(ArchiveImport.MaxPackageFileNameLength);

        builder.Property(import => import.PackagePath)
            .HasMaxLength(400);

        builder.Property(import => import.CreatedByUserId)
            .IsRequired()
            .HasMaxLength(450);

        // Per-file notes, read and written with the row: one JSON column.
        builder.PrimitiveCollection(import => import.Messages)
            .IsRequired();

        // The worker looks for imports still waiting at startup.
        builder.HasIndex(import => import.Status);
    }
}
