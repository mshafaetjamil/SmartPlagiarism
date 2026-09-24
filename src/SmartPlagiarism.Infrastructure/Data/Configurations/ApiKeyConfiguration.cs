using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.HasKey(key => key.Id);

        builder.Property(key => key.Name)
            .IsRequired()
            .HasMaxLength(ApiKey.MaxNameLength);

        // Every request looks its key up by prefix, so this is the hot index.
        builder.Property(key => key.Prefix)
            .IsRequired()
            .HasMaxLength(ApiKey.PrefixLength)
            .IsFixedLength()
            .IsUnicode(false);

        builder.HasIndex(key => key.Prefix).IsUnique();

        // Hex SHA-256: always 64 ASCII characters.
        builder.Property(key => key.KeyHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength()
            .IsUnicode(false);

        builder.Property(key => key.CreatedByUserId)
            .HasMaxLength(450);
    }
}
