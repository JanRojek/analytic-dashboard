using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalyticDashboard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportJobLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAtUtc",
                table: "import_jobs",
                type: "timestamp with time zone",
                nullable: true);

            // Pre-lease workers must be stopped before this migration; their Running jobs can then be recovered.
            migrationBuilder.Sql("""
                UPDATE import_jobs
                SET "LeaseExpiresAtUtc" = clock_timestamp()
                WHERE "Status" = 'Running';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_import_jobs_LeaseExpiresAtUtc_Id",
                table: "import_jobs",
                columns: new[] { "LeaseExpiresAtUtc", "Id" },
                filter: "\"Status\" = 'Running'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_import_jobs_LeaseExpiresAtUtc_Id",
                table: "import_jobs");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "import_jobs");
        }
    }
}
