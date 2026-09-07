using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalyticDashboard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDatasetVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColumnCount",
                table: "datasets");

            migrationBuilder.DropColumn(
                name: "OriginalFileName",
                table: "datasets");

            migrationBuilder.DropColumn(
                name: "RowCount",
                table: "datasets");

            migrationBuilder.DropColumn(
                name: "StoredPath",
                table: "datasets");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "datasets",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentVersionId",
                table: "datasets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "dataset_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: true),
                    ColumnCount = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dataset_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dataset_versions_datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_datasets_CurrentVersionId",
                table: "datasets",
                column: "CurrentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_dataset_versions_DatasetId_CreatedAtUtc_Id",
                table: "dataset_versions",
                columns: new[] { "DatasetId", "CreatedAtUtc", "Id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_dataset_versions_DatasetId_VersionNumber",
                table: "dataset_versions",
                columns: new[] { "DatasetId", "VersionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_datasets_dataset_versions_CurrentVersionId",
                table: "datasets",
                column: "CurrentVersionId",
                principalTable: "dataset_versions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_datasets_dataset_versions_CurrentVersionId",
                table: "datasets");

            migrationBuilder.DropTable(
                name: "dataset_versions");

            migrationBuilder.DropIndex(
                name: "IX_datasets_CurrentVersionId",
                table: "datasets");

            migrationBuilder.DropColumn(
                name: "CurrentVersionId",
                table: "datasets");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "datasets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<int>(
                name: "ColumnCount",
                table: "datasets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                table: "datasets",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RowCount",
                table: "datasets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StoredPath",
                table: "datasets",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }
    }
}
