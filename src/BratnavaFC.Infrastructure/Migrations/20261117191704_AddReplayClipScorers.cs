using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReplayClipScorers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReplayClipScorers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClipId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplayClipScorers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplayClipScorers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReplayClipScorers_ReplayClips_ClipId",
                        column: x => x.ClipId,
                        principalTable: "ReplayClips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplayClipScorers_ClipId",
                table: "ReplayClipScorers",
                column: "ClipId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReplayClipScorers_GroupId",
                table: "ReplayClipScorers",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplayClipScorers_PlayerId",
                table: "ReplayClipScorers",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplayClipScorers");
        }
    }
}
