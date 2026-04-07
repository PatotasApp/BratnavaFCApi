using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAbsences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AutoRejectedByAbsenceId",
                table: "MatchPlayers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserAbsences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AbsenceType = table.Column<short>(type: "smallint", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAbsences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAbsences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchPlayers_AutoRejectedByAbsenceId",
                table: "MatchPlayers",
                column: "AutoRejectedByAbsenceId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAbsences_UserId_StartDate_EndDate",
                table: "UserAbsences",
                columns: new[] { "UserId", "StartDate", "EndDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_MatchPlayers_UserAbsences_AutoRejectedByAbsenceId",
                table: "MatchPlayers",
                column: "AutoRejectedByAbsenceId",
                principalTable: "UserAbsences",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MatchPlayers_UserAbsences_AutoRejectedByAbsenceId",
                table: "MatchPlayers");

            migrationBuilder.DropTable(
                name: "UserAbsences");

            migrationBuilder.DropIndex(
                name: "IX_MatchPlayers_AutoRejectedByAbsenceId",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "AutoRejectedByAbsenceId",
                table: "MatchPlayers");
        }
    }
}
