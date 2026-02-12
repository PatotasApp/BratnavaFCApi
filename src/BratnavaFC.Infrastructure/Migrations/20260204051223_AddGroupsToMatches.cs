using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupsToMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<short>(
                name: "Team",
                table: "MatchPlayers",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0,
                oldClrType: typeof(short),
                oldType: "smallint");

            // Add GroupId NULLABLE primeiro (sem default Guid.Empty)
            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "MatchPlayers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "Matches",
                type: "uuid",
                nullable: true);

            // 1) Backfill MatchPlayers.GroupId = Players.GroupId
            migrationBuilder.Sql(@"
UPDATE ""MatchPlayers"" mp
SET ""GroupId"" = p.""GroupId""
FROM ""Players"" p
WHERE p.""Id"" = mp.""PlayerId""
  AND mp.""GroupId"" IS NULL;
");

            // 2) Backfill Matches.GroupId = algum GroupId dos MatchPlayers
            // Postgres nao suporta MIN(uuid), entao fazemos MIN(text)::uuid
            migrationBuilder.Sql(@"
UPDATE ""Matches"" m
SET ""GroupId"" = sub.""GroupId""
FROM (
    SELECT
        mp.""MatchId"",
        MIN(mp.""GroupId""::text)::uuid AS ""GroupId""
    FROM ""MatchPlayers"" mp
    WHERE mp.""GroupId"" IS NOT NULL
    GROUP BY mp.""MatchId""
) sub
WHERE m.""Id"" = sub.""MatchId""
  AND m.""GroupId"" IS NULL;
");

            // 3) Se sobrar Match antigo sem GroupId (normalmente sem MatchPlayers),
            // garante que existe pelo menos 1 Group e seta um padrao
            migrationBuilder.Sql(@"
INSERT INTO ""Groups"" (""Id"", ""CreateDate"", ""Name"", ""Status"")
SELECT '11111111-1111-1111-1111-111111111111', NOW(), 'Legacy', 1
WHERE NOT EXISTS (SELECT 1 FROM ""Groups"");
");

            migrationBuilder.Sql(@"
UPDATE ""Matches""
SET ""GroupId"" = (
    SELECT ""Id""
    FROM ""Groups""
    ORDER BY ""CreateDate"" NULLS LAST, ""Id""
    LIMIT 1
)
WHERE ""GroupId"" IS NULL;
");

            // 4) Agora torna NOT NULL
            migrationBuilder.AlterColumn<Guid>(
                name: "GroupId",
                table: "MatchPlayers",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GroupId",
                table: "Matches",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // Indexes
            migrationBuilder.CreateIndex(
                name: "IX_MatchPlayers_GroupId",
                table: "MatchPlayers",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPlayers_MatchId_PlayerId_GroupId",
                table: "MatchPlayers",
                columns: new[] { "MatchId", "PlayerId", "GroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matches_GroupId_PlayedAt",
                table: "Matches",
                columns: new[] { "GroupId", "PlayedAt" });

            // FKs
            migrationBuilder.AddForeignKey(
                name: "FK_Matches_Groups_GroupId",
                table: "Matches",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatchPlayers_Groups_GroupId",
                table: "MatchPlayers",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Matches_Groups_GroupId",
                table: "Matches");

            migrationBuilder.DropForeignKey(
                name: "FK_MatchPlayers_Groups_GroupId",
                table: "MatchPlayers");

            migrationBuilder.DropIndex(
                name: "IX_MatchPlayers_GroupId",
                table: "MatchPlayers");

            migrationBuilder.DropIndex(
                name: "IX_MatchPlayers_MatchId_PlayerId_GroupId",
                table: "MatchPlayers");

            migrationBuilder.DropIndex(
                name: "IX_Matches_GroupId_PlayedAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Matches");

            migrationBuilder.AlterColumn<short>(
                name: "Team",
                table: "MatchPlayers",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldDefaultValue: (short)0);
        }
    }
}
