using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class DocumentFingerprintConfiguration : IEntityTypeConfiguration<DocumentFingerprint>
{
    public void Configure(EntityTypeBuilder<DocumentFingerprint> builder)
    {
        builder.HasKey(fingerprint => fingerprint.Id);

        // varbinary(max): packed UInt64 hashes, read and written as a whole set.
        builder.Property(fingerprint => fingerprint.Fingerprints)
            .IsRequired();

        builder.HasOne(fingerprint => fingerprint.UploadedFile)
            .WithOne(file => file.Fingerprint)
            .HasForeignKey<DocumentFingerprint>(fingerprint => fingerprint.UploadedFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
