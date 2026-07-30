using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class SemesterConfiguration : IEntityTypeConfiguration<Semester>
{
    public void Configure(EntityTypeBuilder<Semester> builder)
    {
        builder.HasKey(semester => semester.Id);

        builder.Property(semester => semester.Name)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(semester => semester.Name).IsUnique();

        // Supports "which semester is current" lookups.
        builder.HasIndex(semester => semester.StartDateUtc);
    }
}
