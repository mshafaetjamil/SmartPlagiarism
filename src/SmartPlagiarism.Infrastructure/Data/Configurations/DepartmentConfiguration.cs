using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.HasKey(department => department.Id);

        builder.Property(department => department.Code)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(department => department.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(department => department.Code).IsUnique();
        builder.HasIndex(department => department.Name).IsUnique();
    }
}
