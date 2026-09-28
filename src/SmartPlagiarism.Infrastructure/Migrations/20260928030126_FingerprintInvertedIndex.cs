using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPlagiarism.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FingerprintInvertedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FingerprintHashes",
                columns: table => new
                {
                    Hash = table.Column<long>(type: "bigint", nullable: false),
                    DocumentFingerprintId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FingerprintHashes", x => new { x.Hash, x.DocumentFingerprintId });
                    table.ForeignKey(
                        name: "FK_FingerprintHashes_DocumentFingerprints_DocumentFingerprintId",
                        column: x => x.DocumentFingerprintId,
                        principalTable: "DocumentFingerprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FingerprintHashes_DocumentFingerprintId",
                table: "FingerprintHashes",
                column: "DocumentFingerprintId");

            // Index the fingerprints already stored. Each set is packed in
            // DocumentFingerprints.Fingerprints as consecutive little-endian 64-bit
            // values; SQL Server reads binary(8) as big-endian, so each value's
            // bytes are put back in reverse order before the cast. The signed
            // bigint then has the same bits as the engine's unsigned hash, as the
            // indexer writes it.
            migrationBuilder.Sql("""
                DECLARE @longest int = (SELECT ISNULL(MAX(DATALENGTH(Fingerprints)) / 8, 0) FROM DocumentFingerprints);

                WITH Numbers AS (
                    SELECT TOP (@longest) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS Position
                    FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b)
                INSERT INTO FingerprintHashes (Hash, DocumentFingerprintId)
                SELECT DISTINCT
                    CAST(SUBSTRING(f.Fingerprints, n.Position * 8 + 8, 1) + SUBSTRING(f.Fingerprints, n.Position * 8 + 7, 1) + SUBSTRING(f.Fingerprints, n.Position * 8 + 6, 1) + SUBSTRING(f.Fingerprints, n.Position * 8 + 5, 1) + SUBSTRING(f.Fingerprints, n.Position * 8 + 4, 1) + SUBSTRING(f.Fingerprints, n.Position * 8 + 3, 1) + SUBSTRING(f.Fingerprints, n.Position * 8 + 2, 1) + SUBSTRING(f.Fingerprints, n.Position * 8 + 1, 1) AS bigint),
                    f.Id
                FROM DocumentFingerprints AS f
                JOIN Numbers AS n ON n.Position < DATALENGTH(f.Fingerprints) / 8;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FingerprintHashes");
        }
    }
}
