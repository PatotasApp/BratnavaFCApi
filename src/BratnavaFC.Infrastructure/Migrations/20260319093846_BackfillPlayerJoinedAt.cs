using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillPlayerJoinedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Jogadores promovidos de convidado via convite:
            //    usa a data em que o convite foi aceito (GroupInvites.UpdateDate)
            migrationBuilder.Sql("""
                UPDATE "Players" p
                SET "JoinedAt" = gi."UpdateDate"
                FROM "GroupInvites" gi
                WHERE gi."GuestPlayerId" = p."Id"
                  AND gi."Status"        = 2
                  AND p."IsGuest"        = false
                  AND p."JoinedAt"       IS NULL
                  AND gi."UpdateDate"    IS NOT NULL;
                """);

            // 2. Demais mensalistas sem JoinedAt (criados diretamente ou sem invite registrado):
            //    usa CreateDate, que para criação direta já é a data de entrada
            migrationBuilder.Sql("""
                UPDATE "Players"
                SET    "JoinedAt"  = "CreateDate"
                WHERE  "IsGuest"   = false
                  AND  "UserId"    IS NOT NULL
                  AND  "JoinedAt"  IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Backfill não é revertível de forma segura — apenas limpa JoinedAt dos mensalistas
            migrationBuilder.Sql("""
                UPDATE "Players"
                SET    "JoinedAt" = NULL
                WHERE  "IsGuest"  = false
                  AND  "UserId"   IS NOT NULL;
                """);
        }
    }
}
