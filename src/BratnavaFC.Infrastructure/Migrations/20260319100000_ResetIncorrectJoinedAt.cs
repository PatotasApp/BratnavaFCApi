using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ResetIncorrectJoinedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // O backfill anterior usou GroupInvites.UpdateDate (data de aceitação do invite)
            // como JoinedAt. Para convidados que estavam no grupo desde antes da aceitação,
            // isso resultou em JoinedAt posterior ao CreateDate, ocultando meses anteriores.
            // O PaymentService agora usa CreateDate diretamente, então apenas limpamos
            // os JoinedAt que ficaram incorretos (JoinedAt mais de 30 dias após CreateDate).
            migrationBuilder.Sql("""
                UPDATE "Players"
                SET    "JoinedAt" = NULL
                WHERE  "JoinedAt" IS NOT NULL
                  AND  "JoinedAt" > "CreateDate" + INTERVAL '30 days';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Não há como reverter — o estado original de JoinedAt era NULL
        }
    }
}
