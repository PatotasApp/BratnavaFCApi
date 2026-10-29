using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionshipLinkedPoll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LinkedPollId",
                table: "Championships",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Championships_LinkedPollId",
                table: "Championships",
                column: "LinkedPollId");

            migrationBuilder.AddForeignKey(
                name: "FK_Championships_Polls_LinkedPollId",
                table: "Championships",
                column: "LinkedPollId",
                principalTable: "Polls",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Championships_Polls_LinkedPollId",
                table: "Championships");

            migrationBuilder.DropIndex(
                name: "IX_Championships_LinkedPollId",
                table: "Championships");

            migrationBuilder.DropColumn(
                name: "LinkedPollId",
                table: "Championships");
        }
    }
}
