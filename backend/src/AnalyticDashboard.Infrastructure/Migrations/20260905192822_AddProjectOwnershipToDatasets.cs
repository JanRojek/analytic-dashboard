using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalyticDashboard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectOwnershipToDatasets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_datasets_CreatedAtUtc",
                table: "datasets");

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "datasets",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_datasets_ProjectId_CreatedAtUtc_Id",
                table: "datasets",
                columns: new[] { "ProjectId", "CreatedAtUtc", "Id" },
                descending: new[] { false, true, false });

            migrationBuilder.AddForeignKey(
                name: "FK_datasets_projects_ProjectId",
                table: "datasets",
                column: "ProjectId",
                principalTable: "projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_datasets_projects_ProjectId",
                table: "datasets");

            migrationBuilder.DropIndex(
                name: "IX_datasets_ProjectId_CreatedAtUtc_Id",
                table: "datasets");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "datasets");

            migrationBuilder.CreateIndex(
                name: "IX_datasets_CreatedAtUtc",
                table: "datasets",
                column: "CreatedAtUtc");
        }
    }
}
