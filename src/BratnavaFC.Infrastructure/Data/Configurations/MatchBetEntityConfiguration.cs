using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class MatchBetEntityConfiguration : IEntityTypeConfiguration<MatchBetEntity>
{
    public void Configure(EntityTypeBuilder<MatchBetEntity> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.GroupId).IsRequired();
        b.Property(x => x.MatchId).IsRequired();
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.IsResolved).IsRequired().HasDefaultValue(false);

        b.HasMany(x => x.Selections)
         .WithOne(x => x.Bet)
         .HasForeignKey(x => x.BetId)
         .OnDelete(DeleteBehavior.Cascade);

        // Um usuário faz no máximo uma aposta por partida
        b.HasIndex(x => new { x.MatchId, x.UserId }).IsUnique();
        b.HasIndex(x => new { x.GroupId, x.MatchId });
    }
}
