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

public class FingerprintHashConfiguration : IEntityTypeConfiguration<FingerprintHash>
{
    public void Configure(EntityTypeBuilder<FingerprintHash> builder)
    {
        // Clustered on the hash first: shortlisting looks rows up by hash and
        // counts them per document, so every lookup is a seek.
        builder.HasKey(row => new { row.Hash, row.DocumentFingerprintId });

        // Replacing or deleting one document's set goes by the document.
        builder.HasIndex(row => row.DocumentFingerprintId);

        builder.HasOne(row => row.DocumentFingerprint)
            .WithMany(fingerprint => fingerprint.Hashes)
            .HasForeignKey(row => row.DocumentFingerprintId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
