using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(log => log.Id);

        // Matches the Identity key length, but is not a foreign key - see AuditLog.
        builder.Property(log => log.UserId)
            .HasMaxLength(450);

        builder.Property(log => log.Action)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(log => log.EntityType)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(log => log.EntityId)
            .HasMaxLength(64);

        // The admin log viewer filters by time, by entity, and by user.
        builder.HasIndex(log => log.TimestampUtc);
        builder.HasIndex(log => new { log.EntityType, log.EntityId });
        builder.HasIndex(log => log.UserId);
    }
}
