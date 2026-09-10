using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalyticDashboard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorDashboardAndWidgetModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Dashboards_datasets_DatasetId",
                table: "Dashboards");

            migrationBuilder.DropColumn(
                name: "XColumn",
                table: "Widgets");

            migrationBuilder.DropColumn(
                name: "YColumn",
                table: "Widgets");

            migrationBuilder.RenameColumn(
                name: "DatasetId",
                table: "Dashboards",
                newName: "ProjectId");

            migrationBuilder.RenameIndex(
                name: "IX_Dashboards_DatasetId",
                table: "Dashboards",
                newName: "IX_Dashboards_ProjectId");

            migrationBuilder.AlterColumn<string>(
                name: "Aggregation",
                table: "Widgets",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<Guid>(
                name: "DatasetId",
                table: "Widgets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "GroupByColumn",
                table: "Widgets",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MeasureColumn",
                table: "Widgets",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Dashboards",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateIndex(
                name: "IX_Widgets_DatasetId",
                table: "Widgets",
                column: "DatasetId");

            migrationBuilder.AddForeignKey(
                name: "FK_Dashboards_projects_ProjectId",
                table: "Dashboards",
                column: "ProjectId",
                principalTable: "projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Widgets_datasets_DatasetId",
                table: "Widgets",
                column: "DatasetId",
                principalTable: "datasets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Dashboards_projects_ProjectId",
                table: "Dashboards");

            migrationBuilder.DropForeignKey(
                name: "FK_Widgets_datasets_DatasetId",
                table: "Widgets");

            migrationBuilder.DropIndex(
                name: "IX_Widgets_DatasetId",
                table: "Widgets");

            migrationBuilder.DropColumn(
                name: "DatasetId",
                table: "Widgets");

            migrationBuilder.DropColumn(
                name: "GroupByColumn",
                table: "Widgets");

            migrationBuilder.DropColumn(
                name: "MeasureColumn",
                table: "Widgets");

            migrationBuilder.RenameColumn(
                name: "ProjectId",
                table: "Dashboards",
                newName: "DatasetId");

            migrationBuilder.RenameIndex(
                name: "IX_Dashboards_ProjectId",
                table: "Dashboards",
                newName: "IX_Dashboards_DatasetId");

            migrationBuilder.AlterColumn<string>(
                name: "Aggregation",
                table: "Widgets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "XColumn",
                table: "Widgets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YColumn",
                table: "Widgets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Dashboards",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddForeignKey(
                name: "FK_Dashboards_datasets_DatasetId",
                table: "Dashboards",
                column: "DatasetId",
                principalTable: "datasets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
