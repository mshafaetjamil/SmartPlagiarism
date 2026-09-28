using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPlagiarism.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Archive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "SubmissionId",
                table: "UploadedFiles",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "ArchiveDocumentId",
                table: "UploadedFiles",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArchiveImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackageFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    PackagePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DefaultYear = table.Column<int>(type: "int", nullable: true),
                    DefaultDepartmentId = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalFiles = table.Column<int>(type: "int", nullable: false),
                    ImportedFiles = table.Column<int>(type: "int", nullable: false),
                    SkippedFiles = table.Column<int>(type: "int", nullable: false),
                    Messages = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveImports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArchiveDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Year = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    CourseCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ArchiveImportId = table.Column<int>(type: "int", nullable: true),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchiveDocuments_ArchiveImports_ArchiveImportId",
                        column: x => x.ArchiveImportId,
                        principalTable: "ArchiveImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ArchiveDocuments_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadedFiles_ArchiveDocumentId",
                table: "UploadedFiles",
                column: "ArchiveDocumentId",
                unique: true,
                filter: "[ArchiveDocumentId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UploadedFiles_OneOwner",
                table: "UploadedFiles",
                sql: "([SubmissionId] IS NULL AND [ArchiveDocumentId] IS NOT NULL) OR ([SubmissionId] IS NOT NULL AND [ArchiveDocumentId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveDocuments_ArchiveImportId",
                table: "ArchiveDocuments",
                column: "ArchiveImportId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveDocuments_DepartmentId",
                table: "ArchiveDocuments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveDocuments_Year",
                table: "ArchiveDocuments",
                column: "Year");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveImports_Status",
                table: "ArchiveImports",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_UploadedFiles_ArchiveDocuments_ArchiveDocumentId",
                table: "UploadedFiles",
                column: "ArchiveDocumentId",
                principalTable: "ArchiveDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UploadedFiles_ArchiveDocuments_ArchiveDocumentId",
                table: "UploadedFiles");

            migrationBuilder.DropTable(
                name: "ArchiveDocuments");

            migrationBuilder.DropTable(
                name: "ArchiveImports");

            migrationBuilder.DropIndex(
                name: "IX_UploadedFiles_ArchiveDocumentId",
                table: "UploadedFiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UploadedFiles_OneOwner",
                table: "UploadedFiles");

            migrationBuilder.DropColumn(
                name: "ArchiveDocumentId",
                table: "UploadedFiles");

            migrationBuilder.AlterColumn<int>(
                name: "SubmissionId",
                table: "UploadedFiles",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
