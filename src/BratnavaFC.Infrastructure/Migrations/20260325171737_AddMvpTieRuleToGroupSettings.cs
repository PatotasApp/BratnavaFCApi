using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMvpTieRuleToGroupSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MvpTieMaxPlayers",
                table: "GroupSettings",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<short>(
                name: "MvpTieRule",
                table: "GroupSettings",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1); // 1 = AllMvp
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MvpTieMaxPlayers",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "MvpTieRule",
                table: "GroupSettings");
        }
    }
}
