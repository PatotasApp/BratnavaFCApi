using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Move a logo do grupo do Postgres para o bucket público de imagens no Cloudflare R2,
    /// pelo mesmo caminho já percorrido pelo avatar em AddProfilePhotoKey. A coluna passa a
    /// guardar a object key; a URL pública é composta em runtime a partir dela.
    ///
    /// A PERDA DAS LOGOS EXISTENTES É INTENCIONAL E FOI ACORDADA: não há backfill porque os
    /// grupos atuais são de teste e as imagens serão reenviadas. O Down recria as colunas
    /// vazias — os bytes não voltam.
    ///
    /// LogoUpdatedAt sobrevive como metadado; o cache-busting passou a ser o GUID na key.
    /// </summary>
    public partial class MoveGroupLogoToR2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoContentType",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "LogoData",
                table: "Groups");

            migrationBuilder.AddColumn<string>(
                name: "LogoKey",
                table: "Groups",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoKey",
                table: "Groups");

            migrationBuilder.AddColumn<string>(
                name: "LogoContentType",
                table: "Groups",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "LogoData",
                table: "Groups",
                type: "bytea",
                nullable: true);
        }
    }
}
