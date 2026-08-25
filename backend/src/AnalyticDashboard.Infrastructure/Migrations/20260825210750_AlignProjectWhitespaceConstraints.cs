using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalyticDashboard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AlignProjectWhitespaceConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_Name_NotBlank",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_Name_Trimmed",
                table: "projects");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_Name_NotBlank",
                table: "projects",
                sql: "btrim(\"Name\"::text, E' \\u0009\\u000A\\u000B\\u000C\\u000D\\u0020\\u0085\\u00A0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000') <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_Name_Trimmed",
                table: "projects",
                sql: "\"Name\"::text = btrim(\"Name\"::text, E' \\u0009\\u000A\\u000B\\u000C\\u000D\\u0020\\u0085\\u00A0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_Name_NotBlank",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_projects_Name_Trimmed",
                table: "projects");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_Name_NotBlank",
                table: "projects",
                sql: "btrim(\"Name\"::text) <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "CK_projects_Name_Trimmed",
                table: "projects",
                sql: "\"Name\"::text = btrim(\"Name\"::text)");
        }
    }
}
