using System;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Reconstruída a partir do schema de produção — arquivo original perdido, Id preservado
    /// para produção pular. A entidade correspondente vive em outra branch; aqui só o schema.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260718003431_AddExitDebtAlerts")]
    public partial class AddExitDebtAlerts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExitDebtAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<decimal>(type: "numeric", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExitDebtAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExitDebtAlerts_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExitDebtAlerts_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExitDebtAlerts_Users_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExitDebtAlerts_GroupId_ResolvedAt_CreateDate",
                table: "ExitDebtAlerts",
                columns: new[] { "GroupId", "ResolvedAt", "CreateDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ExitDebtAlerts_PlayerId",
                table: "ExitDebtAlerts",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ExitDebtAlerts_ResolvedByUserId",
                table: "ExitDebtAlerts",
                column: "ResolvedByUserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ExitDebtAlerts");
        }

        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
        }
    }
}
