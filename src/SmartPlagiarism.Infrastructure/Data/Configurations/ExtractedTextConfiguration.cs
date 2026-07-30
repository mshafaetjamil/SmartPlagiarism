using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPlagiarism.Core.Entities;

namespace SmartPlagiarism.Infrastructure.Data.Configurations;

public class ExtractedTextConfiguration : IEntityTypeConfiguration<ExtractedText>
{
    public void Configure(EntityTypeBuilder<ExtractedText> builder)
    {
        builder.HasKey(text => text.Id);

        // nvarchar(max): a full report's text has no useful length ceiling.
        builder.Property(text => text.FullText)
            .IsRequired();

        // One extraction per file; EF creates the unique index for this 1:1.
        builder.HasOne(text => text.UploadedFile)
            .WithOne(file => file.ExtractedText)
            .HasForeignKey<ExtractedText>(text => text.UploadedFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
