using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPlagiarism.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnalysisAttempts",
                table: "Submissions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnalysisAttempts",
                table: "Submissions");
        }
    }
}
