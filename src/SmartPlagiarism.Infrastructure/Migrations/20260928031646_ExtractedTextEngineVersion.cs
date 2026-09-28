using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPlagiarism.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtractedTextEngineVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EngineVersion",
                table: "ExtractedTexts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EngineVersion",
                table: "ExtractedTexts");
        }
    }
}
