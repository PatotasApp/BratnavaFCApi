using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonTitleScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeasonTitles_GroupId_Season_Category_PlayerId",
                table: "SeasonTitles");

            migrationBuilder.AddColumn<short>(
                name: "Scope",
                table: "SeasonTitles",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<Guid>(
                name: "SeasonId",
                table: "SeasonTitles",
                type: "uuid",
                nullable: true);

            // Títulos já selados eram do ano civil, que agora é a temporada Ano/1 da patota.
            migrationBuilder.Sql("""
                UPDATE "SeasonTitles" t
                SET "SeasonId" = s."Id", "Scope" = 1
                FROM "Seasons" s
                WHERE s."GroupId" = t."GroupId" AND s."Year" = t."Season" AND s."Number" = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SeasonTitles_Season",
                table: "SeasonTitles",
                columns: new[] { "GroupId", "SeasonId", "Category", "PlayerId" },
                unique: true,
                filter: "\"Scope\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonTitles_SeasonId",
                table: "SeasonTitles",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonTitles_Year",
                table: "SeasonTitles",
                columns: new[] { "GroupId", "Season", "Category", "PlayerId" },
                unique: true,
                filter: "\"Scope\" = 2");

            migrationBuilder.AddForeignKey(
                name: "FK_SeasonTitles_Seasons_SeasonId",
                table: "SeasonTitles",
                column: "SeasonId",
                principalTable: "Seasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SeasonTitles_Seasons_SeasonId",
                table: "SeasonTitles");

            migrationBuilder.DropIndex(
                name: "IX_SeasonTitles_Season",
                table: "SeasonTitles");

            migrationBuilder.DropIndex(
                name: "IX_SeasonTitles_SeasonId",
                table: "SeasonTitles");

            migrationBuilder.DropIndex(
                name: "IX_SeasonTitles_Year",
                table: "SeasonTitles");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "SeasonTitles");

            migrationBuilder.DropColumn(
                name: "SeasonId",
                table: "SeasonTitles");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonTitles_GroupId_Season_Category_PlayerId",
                table: "SeasonTitles",
                columns: new[] { "GroupId", "Season", "Category", "PlayerId" },
                unique: true);
        }
    }
}
