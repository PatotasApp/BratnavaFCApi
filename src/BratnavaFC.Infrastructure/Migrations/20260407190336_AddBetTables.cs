using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBetTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchBets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchBets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserBetBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TotalBets = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TotalCorrect = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBetBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatchBetSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<short>(type: "smallint", nullable: false),
                    PredictedValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FichasWagered = table.Column<int>(type: "integer", nullable: false),
                    ActualValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FichasEarned = table.Column<int>(type: "integer", nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: true),
                    IsPartialCredit = table.Column<bool>(type: "boolean", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchBetSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchBetSelections_MatchBets_BetId",
                        column: x => x.BetId,
                        principalTable: "MatchBets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchBets_GroupId_MatchId",
                table: "MatchBets",
                columns: new[] { "GroupId", "MatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchBets_MatchId_UserId",
                table: "MatchBets",
                columns: new[] { "MatchId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchBetSelections_BetId",
                table: "MatchBetSelections",
                column: "BetId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBetBalances_GroupId_UserId",
                table: "UserBetBalances",
                columns: new[] { "GroupId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchBetSelections");

            migrationBuilder.DropTable(
                name: "UserBetBalances");

            migrationBuilder.DropTable(
                name: "MatchBets");
        }
    }
}
