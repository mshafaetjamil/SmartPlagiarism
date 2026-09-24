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
            .HasMaxLength(Department.MaxCodeLength);

        builder.Property(department => department.Name)
            .IsRequired()
            .HasMaxLength(Department.MaxNameLength);

        builder.HasIndex(department => department.Code).IsUnique();
        builder.HasIndex(department => department.Name).IsUnique();
    }
}
