using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPlagiarism.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MatchComponentScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComponentScoresJson",
                table: "MatchedDocuments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComponentScoresJson",
                table: "MatchedDocuments");
        }
    }
}
