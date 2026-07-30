using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class TeacherProfileConfiguration : IEntityTypeConfiguration<TeacherProfile>
{
    public void Configure(EntityTypeBuilder<TeacherProfile> builder)
    {
        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(profile => profile.EmployeeNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasIndex(profile => profile.EmployeeNumber).IsUnique();

        builder.HasOne(profile => profile.User)
            .WithOne()
            .HasForeignKey<TeacherProfile>(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(profile => profile.Department)
            .WithMany()
            .HasForeignKey(profile => profile.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
