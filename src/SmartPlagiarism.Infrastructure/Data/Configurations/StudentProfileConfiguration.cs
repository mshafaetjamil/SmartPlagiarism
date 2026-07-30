using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class StudentProfileConfiguration : IEntityTypeConfiguration<StudentProfile>
{
    public void Configure(EntityTypeBuilder<StudentProfile> builder)
    {
        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(profile => profile.StudentNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasIndex(profile => profile.StudentNumber).IsUnique();

        // One profile per Identity user.
        builder.HasOne(profile => profile.User)
            .WithOne()
            .HasForeignKey<StudentProfile>(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(profile => profile.Department)
            .WithMany()
            .HasForeignKey(profile => profile.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
