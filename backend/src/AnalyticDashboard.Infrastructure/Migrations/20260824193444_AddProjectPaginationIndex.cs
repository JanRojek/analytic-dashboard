using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalyticDashboard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectPaginationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_projects_OwnerId_CreatedAtUtc_Id",
                table: "projects",
                columns: new[] { "OwnerId", "CreatedAtUtc", "Id" },
                descending: new[] { false, true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_projects_OwnerId_CreatedAtUtc_Id",
                table: "projects");
        }
    }
}
