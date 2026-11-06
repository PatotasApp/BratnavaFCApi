using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupPublicPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PublicPageEnabled",
                table: "GroupSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublicPageEnabled",
                table: "GroupSettings");
        }
    }
}
