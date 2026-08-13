using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class PlayerEntityConfiguration : IEntityTypeConfiguration<PlayerEntity>
{
    public void Configure(EntityTypeBuilder<PlayerEntity> builder)
    {
        builder.HasKey(x => x.Id);

        // Um usuário tem no máximo um player por grupo. Os três caminhos que inserem player com
        // dono já checam isso em código (PlayerService, CreateInviteAsync e AcceptInviteAsync),
        // mas a checagem é um SELECT seguido de INSERT — duas requests simultâneas passam pelas
        // duas leituras antes de qualquer escrita. Player duplicado propaga para estatística e
        // financeiro, então a garantia precisa estar no banco.
        //
        // Filtrado porque convidado tem UserId nulo, e um grupo tem muitos convidados.
        builder.HasIndex(x => new { x.GroupId, x.UserId })
            .IsUnique()
            .HasFilter("\"UserId\" IS NOT NULL");

        // Declarado explicitamente porque o EF o considera redundante com o composto acima e o
        // dropa sozinho. Não é redundante: o composto é PARCIAL, e o Postgres não usa índice
        // parcial para consulta que só filtra GroupId — ela inclui convidados, que estão fora
        // do índice. E filtrar player por grupo é o acesso mais comum do sistema.
        builder.HasIndex(x => x.GroupId);

        builder.Property(x => x.Name).IsRequired();
        builder.Property(x => x.IsGoalkeeper).IsRequired();
        builder.Property(x => x.IsGuest).IsRequired().HasDefaultValue(false);

        builder.HasOne(x => x.User)
            .WithMany(x => x.Players)
            .HasForeignKey(x => x.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Group)
            .WithMany(x => x.Players)
            .HasForeignKey(x => x.GroupId);

        builder.HasMany(x => x.MatchPlayers)
            .WithOne(x => x.Player)
            .HasForeignKey(x => x.PlayerId);

        builder.Property(x => x.Status)
            .HasConversion<short>()
            .HasDefaultValue(Status.Active)
            .IsRequired();

        builder.Property(x => x.InactivatedAt);
    }
}
