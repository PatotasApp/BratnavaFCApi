using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankIcons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Rank1Icon",
                table: "GroupSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rank2Icon",
                table: "GroupSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rank3Icon",
                table: "GroupSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Rank1Icon",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "Rank2Icon",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "Rank3Icon",
                table: "GroupSettings");
        }
    }
}
