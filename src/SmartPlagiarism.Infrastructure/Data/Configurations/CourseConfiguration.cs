using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.HasKey(course => course.Id);

        builder.Property(course => course.Code)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(course => course.Title)
            .IsRequired()
            .HasMaxLength(200);

        // Course codes repeat across departments, so uniqueness is per department.
        builder.HasIndex(course => new { course.DepartmentId, course.Code }).IsUnique();

        builder.HasOne(course => course.Department)
            .WithMany(department => department.Courses)
            .HasForeignKey(course => course.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
