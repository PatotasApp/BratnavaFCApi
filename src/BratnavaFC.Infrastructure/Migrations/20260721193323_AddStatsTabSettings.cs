using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStatsTabSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowStatsClassificationTab",
                table: "GroupSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowStatsGeneralTab",
                table: "GroupSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowStatsPerMatchTab",
                table: "GroupSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowStatsClassificationTab",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "ShowStatsGeneralTab",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "ShowStatsPerMatchTab",
                table: "GroupSettings");
        }
    }
}
