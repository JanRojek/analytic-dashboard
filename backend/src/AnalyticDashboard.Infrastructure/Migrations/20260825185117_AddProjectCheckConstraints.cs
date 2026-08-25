using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalyticDashboard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_Name_MaxLength",
                table: "projects",
                sql: "char_length(\"Name\"::text) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_Name_NotBlank",
                table: "projects",
                sql: "btrim(\"Name\"::text) <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_Name_Trimmed",
                table: "projects",
                sql: "\"Name\"::text = btrim(\"Name\"::text)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_OwnerId_NotEmpty",
                table: "projects",
                sql: "\"OwnerId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_Name_MaxLength",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_Name_NotBlank",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_Name_Trimmed",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_OwnerId_NotEmpty",
                table: "projects");
        }
    }
}
