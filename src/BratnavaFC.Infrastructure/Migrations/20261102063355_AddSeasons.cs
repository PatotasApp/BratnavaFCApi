using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsPreSeason = table.Column<bool>(type: "boolean", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seasons_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_GroupId_Open",
                table: "Seasons",
                column: "GroupId",
                unique: true,
                filter: "\"EndsAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_GroupId_StartsAt",
                table: "Seasons",
                columns: new[] { "GroupId", "StartsAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_GroupId_Year_Number",
                table: "Seasons",
                columns: new[] { "GroupId", "Year", "Number" },
                unique: true);

            // Uma temporada por ano civil (Brasília) para cada patota, do ano do primeiro jogo
            // (partida ou campeonato) até o ano atual, que fica aberto. Patota sem jogos ganha
            // só a do ano atual. Ids fixos por patota e ano. Nenhuma foi fechada por admin.
            migrationBuilder.Sql("""
                WITH atual AS (
                    SELECT extract(year FROM now() AT TIME ZONE 'America/Sao_Paulo')::int AS ano
                ),
                inicio AS (
                    SELECT g."Id" AS group_id,
                           least(
                               coalesce((SELECT min(extract(year FROM m."PlayedAt" AT TIME ZONE 'America/Sao_Paulo'))::int
                                         FROM "Matches" m WHERE m."GroupId" = g."Id"), atual.ano),
                               coalesce((SELECT min(extract(year FROM c."PlayedAt" AT TIME ZONE 'America/Sao_Paulo'))::int
                                         FROM "Championships" c WHERE c."GroupId" = g."Id"), atual.ano),
                               atual.ano) AS primeiro_ano,
                           atual.ano AS ano_atual
                    FROM "Groups" g CROSS JOIN atual
                )
                INSERT INTO "Seasons" ("Id", "GroupId", "Year", "Number", "Name", "StartsAt", "EndsAt",
                                       "IsPreSeason", "ClosedAt", "ClosedByUserId", "CreateDate")
                SELECT md5('temporada/' || i.group_id::text || '/' || ano::text)::uuid,
                       i.group_id, ano, 1, NULL,
                       make_timestamptz(ano, 1, 1, 0, 0, 0, 'America/Sao_Paulo'),
                       CASE WHEN ano < i.ano_atual THEN make_timestamptz(ano + 1, 1, 1, 0, 0, 0, 'America/Sao_Paulo') END,
                       false,
                       CASE WHEN ano < i.ano_atual THEN make_timestamptz(ano + 1, 1, 1, 0, 0, 0, 'America/Sao_Paulo') END,
                       NULL, now()
                FROM inicio i
                CROSS JOIN LATERAL generate_series(i.primeiro_ano, i.ano_atual) AS ano
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Seasons");
        }
    }
}
