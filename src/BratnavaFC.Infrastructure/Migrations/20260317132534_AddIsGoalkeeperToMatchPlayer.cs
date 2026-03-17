using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsGoalkeeperToMatchPlayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsGoalkeeper",
                table: "MatchPlayers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: copy IsGoalkeeper from the Players table for existing rows
            migrationBuilder.Sql(@"
                UPDATE ""MatchPlayers"" mp
                SET ""IsGoalkeeper"" = p.""IsGoalkeeper""
                FROM ""Players"" p
                WHERE mp.""PlayerId"" = p.""Id""
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsGoalkeeper",
                table: "MatchPlayers");
        }
    }
}
