using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPlagiarism.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CommonTextPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommonTextPercent",
                table: "PlagiarismResults",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommonTextPercent",
                table: "PlagiarismResults");
        }
    }
}
