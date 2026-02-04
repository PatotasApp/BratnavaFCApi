using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MatchStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFinalized",
                table: "Matches");

            migrationBuilder.AddColumn<short>(
                name: "Status",
                table: "Matches",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "Matches");

            migrationBuilder.AddColumn<bool>(
                name: "IsFinalized",
                table: "Matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
