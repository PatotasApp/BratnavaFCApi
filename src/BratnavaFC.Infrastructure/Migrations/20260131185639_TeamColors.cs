using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TeamColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TeamBGoals",
                table: "Matches",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "TeamAGoals",
                table: "Matches",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "PlaceName",
                table: "Matches",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "TeamAColorId",
                table: "Matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TeamBColorId",
                table: "Matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamColors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HexValue = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamColors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_TeamAColorId",
                table: "Matches",
                column: "TeamAColorId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_TeamBColorId",
                table: "Matches",
                column: "TeamBColorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_TeamColors_TeamAColorId",
                table: "Matches",
                column: "TeamAColorId",
                principalTable: "TeamColors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_TeamColors_TeamBColorId",
                table: "Matches",
                column: "TeamBColorId",
                principalTable: "TeamColors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Matches_TeamColors_TeamAColorId",
                table: "Matches");

            migrationBuilder.DropForeignKey(
                name: "FK_Matches_TeamColors_TeamBColorId",
                table: "Matches");

            migrationBuilder.DropTable(
                name: "TeamColors");

            migrationBuilder.DropIndex(
                name: "IX_Matches_TeamAColorId",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_TeamBColorId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PlaceName",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "TeamAColorId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "TeamBColorId",
                table: "Matches");

            migrationBuilder.AlterColumn<int>(
                name: "TeamBGoals",
                table: "Matches",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TeamAGoals",
                table: "Matches",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
