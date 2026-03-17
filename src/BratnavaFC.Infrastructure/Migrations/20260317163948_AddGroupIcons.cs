using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupIcons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssistIcon",
                table: "GroupSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoalIcon",
                table: "GroupSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoalkeeperIcon",
                table: "GroupSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MvpIcon",
                table: "GroupSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnGoalIcon",
                table: "GroupSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssistIcon",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "GoalIcon",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "GoalkeeperIcon",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "MvpIcon",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "OwnGoalIcon",
                table: "GroupSettings");
        }
    }
}
