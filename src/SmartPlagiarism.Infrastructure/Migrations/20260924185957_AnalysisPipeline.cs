using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPlagiarism.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnalysisError",
                table: "Submissions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UploadedFileId",
                table: "MatchedDocuments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Warnings",
                table: "ExtractedTexts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateIndex(
                name: "IX_MatchedDocuments_UploadedFileId",
                table: "MatchedDocuments",
                column: "UploadedFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_MatchedDocuments_UploadedFiles_UploadedFileId",
                table: "MatchedDocuments",
                column: "UploadedFileId",
                principalTable: "UploadedFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MatchedDocuments_UploadedFiles_UploadedFileId",
                table: "MatchedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_MatchedDocuments_UploadedFileId",
                table: "MatchedDocuments");

            migrationBuilder.DropColumn(
                name: "AnalysisError",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "UploadedFileId",
                table: "MatchedDocuments");

            migrationBuilder.DropColumn(
                name: "Warnings",
                table: "ExtractedTexts");
        }
    }
}
