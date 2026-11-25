using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParticipationHistoryAndMatchWaitingList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // O banco DEV recebeu esta coluna durante uma execução parcial anterior.
            // IF NOT EXISTS mantém a migration segura tanto nele quanto em produção.
            migrationBuilder.Sql(
                "ALTER TABLE \"MatchPlayers\" ADD COLUMN IF NOT EXISTS \"InviteRespondedAt\" timestamp with time zone;");

            migrationBuilder.CreateTable(
                name: "ParticipationActionHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PerformedByAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParticipationActionHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParticipationActionHistory_GroupId_SubjectType_SubjectId_Pl~",
                table: "ParticipationActionHistory",
                columns: new[] { "GroupId", "SubjectType", "SubjectId", "PlayerId", "CreateDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ParticipationActionHistory");

            migrationBuilder.DropColumn(
                name: "InviteRespondedAt",
                table: "MatchPlayers");
        }
    }
}
