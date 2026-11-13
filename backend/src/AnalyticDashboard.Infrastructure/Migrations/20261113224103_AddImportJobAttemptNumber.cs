using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalyticDashboard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportJobAttemptNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AttemptNumber",
                table: "import_jobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttemptNumber",
                table: "import_jobs");
        }
    }
}
