using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsGuestAndNullableUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Goals_MatchPlayers_MatchPlayerEntityId",
                table: "Goals");

            migrationBuilder.DropForeignKey(
                name: "FK_Goals_MatchPlayers_MatchPlayerEntityId1",
                table: "Goals");

            migrationBuilder.DropForeignKey(
                name: "FK_Players_Users_UserId",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_Goals_MatchPlayerEntityId",
                table: "Goals");

            migrationBuilder.DropIndex(
                name: "IX_Goals_MatchPlayerEntityId1",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "MatchPlayerEntityId",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "MatchPlayerEntityId1",
                table: "Goals");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Players",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "IsGuest",
                table: "Players",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Users_UserId",
                table: "Players",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_Users_UserId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "IsGuest",
                table: "Players");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Players",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MatchPlayerEntityId",
                table: "Goals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MatchPlayerEntityId1",
                table: "Goals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Goals_MatchPlayerEntityId",
                table: "Goals",
                column: "MatchPlayerEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_MatchPlayerEntityId1",
                table: "Goals",
                column: "MatchPlayerEntityId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Goals_MatchPlayers_MatchPlayerEntityId",
                table: "Goals",
                column: "MatchPlayerEntityId",
                principalTable: "MatchPlayers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Goals_MatchPlayers_MatchPlayerEntityId1",
                table: "Goals",
                column: "MatchPlayerEntityId1",
                principalTable: "MatchPlayers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Users_UserId",
                table: "Players",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
