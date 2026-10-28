using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalyticDashboard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWidgetLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Layout_Height",
                table: "Widgets",
                type: "integer",
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "Layout_Width",
                table: "Widgets",
                type: "integer",
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "Layout_X",
                table: "Widgets",
                type: "integer",
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "Layout_Y",
                table: "Widgets",
                type: "integer",
                nullable: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Layout_Height",
                table: "Widgets");

            migrationBuilder.DropColumn(
                name: "Layout_Width",
                table: "Widgets");

            migrationBuilder.DropColumn(
                name: "Layout_X",
                table: "Widgets");

            migrationBuilder.DropColumn(
                name: "Layout_Y",
                table: "Widgets");
        }
    }
}
