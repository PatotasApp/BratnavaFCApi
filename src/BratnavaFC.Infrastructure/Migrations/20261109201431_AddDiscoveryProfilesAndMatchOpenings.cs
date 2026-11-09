using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryProfilesAndMatchOpenings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "GuestOrigin",
                table: "Players",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Groups",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsAcceptingPlayers",
                table: "Groups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE "Groups"
                SET "Code" = translate(upper(substr(md5("Id"::text), 1, 8)), '01', 'XY')
                WHERE "Code" = '';
                UPDATE "Players" SET "GuestOrigin" = 1 WHERE "IsGuest" = true AND "GuestOrigin" IS NULL;
                """);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Groups",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileCity",
                table: "Groups",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileDescription",
                table: "Groups",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowMembersOnProfile",
                table: "Groups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MatchOpenings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slots = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchOpenings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchOpenings_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchOpenings_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpeningId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuestPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchApplications_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchApplications_MatchOpenings_OpeningId",
                        column: x => x.OpeningId,
                        principalTable: "MatchOpenings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MatchApplications_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchApplications_Players_GuestPlayerId",
                        column: x => x.GuestPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MatchApplications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_Code",
                table: "Groups",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchApplications_GroupId_Status",
                table: "MatchApplications",
                columns: new[] { "GroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchApplications_GuestPlayerId",
                table: "MatchApplications",
                column: "GuestPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchApplications_MatchId_UserId",
                table: "MatchApplications",
                columns: new[] { "MatchId", "UserId" },
                unique: true,
                filter: "\"Status\" IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_MatchApplications_OpeningId",
                table: "MatchApplications",
                column: "OpeningId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchApplications_UserId_Status",
                table: "MatchApplications",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchOpenings_GroupId",
                table: "MatchOpenings",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchOpenings_MatchId_IsActive",
                table: "MatchOpenings",
                columns: new[] { "MatchId", "IsActive" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchApplications");

            migrationBuilder.DropTable(
                name: "MatchOpenings");

            migrationBuilder.DropIndex(
                name: "IX_Groups_Code",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "GuestOrigin",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "IsAcceptingPlayers",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "ProfileCity",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "ProfileDescription",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "ShowMembersOnProfile",
                table: "Groups");

        }
    }
}
