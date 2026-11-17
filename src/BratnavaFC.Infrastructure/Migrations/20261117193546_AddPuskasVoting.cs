using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPuskasVoting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PuskasPolls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    ClipIdsJson = table.Column<string>(type: "text", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PuskasPolls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PuskasVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PollId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClipId = table.Column<Guid>(type: "uuid", nullable: false),
                    VoterName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    VoterKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PuskasVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PuskasVotes_PuskasPolls_PollId",
                        column: x => x.PollId,
                        principalTable: "PuskasPolls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PuskasVotes_ReplayClips_ClipId",
                        column: x => x.ClipId,
                        principalTable: "ReplayClips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PuskasPolls_Code",
                table: "PuskasPolls",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PuskasPolls_GroupId",
                table: "PuskasPolls",
                column: "GroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PuskasVotes_ClipId",
                table: "PuskasVotes",
                column: "ClipId");

            migrationBuilder.CreateIndex(
                name: "IX_PuskasVotes_PollId",
                table: "PuskasVotes",
                column: "PollId");

            migrationBuilder.CreateIndex(
                name: "IX_PuskasVotes_PollId_VoterKey_ClipId",
                table: "PuskasVotes",
                columns: new[] { "PollId", "VoterKey", "ClipId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PuskasVotes");

            migrationBuilder.DropTable(
                name: "PuskasPolls");
        }
    }
}
