using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Move o avatar do Postgres para o bucket público de avatares no Cloudflare R2. A coluna
    /// passa a guardar a object key; a URL pública é composta em runtime a partir dela.
    ///
    /// A PERDA DAS IMAGENS EXISTENTES É INTENCIONAL E FOI ACORDADA: não há backfill porque os
    /// usuários atuais são de teste e as fotos serão reenviadas. O Down recria as colunas
    /// vazias — é o máximo que dá para oferecer, os bytes não voltam.
    ///
    /// ProfilePhotoUpdatedAt sobrevive: já é exposta em UserDto e UserItemListDto e lida pelos
    /// dois clients. Ela deixa de servir de cache-buster (o GUID na key faz isso) e volta a
    /// ser só o metadado de quando a foto mudou.
    /// </summary>
    public partial class AddProfilePhotoKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfilePhotoContentType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfilePhotoData",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "ProfilePhotoKey",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfilePhotoKey",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "ProfilePhotoContentType",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ProfilePhotoData",
                table: "Users",
                type: "bytea",
                nullable: true);
        }
    }
}
