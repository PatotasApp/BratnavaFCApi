using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class ChampionshipBetEntityConfiguration : IEntityTypeConfiguration<ChampionshipBetEntity>
{
    public void Configure(EntityTypeBuilder<ChampionshipBetEntity> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.GroupId).IsRequired();
        b.Property(x => x.ChampionshipId).IsRequired();
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.IsResolved).IsRequired().HasDefaultValue(false);

        b.HasOne<ChampionshipEntity>()
         .WithMany()
         .HasForeignKey(x => x.ChampionshipId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Selections)
         .WithOne(x => x.Bet)
         .HasForeignKey(x => x.BetId)
         .OnDelete(DeleteBehavior.Cascade);

        // Um usuário faz no máximo uma aposta por campeonato
        b.HasIndex(x => new { x.ChampionshipId, x.UserId }).IsUnique();
        b.HasIndex(x => new { x.GroupId, x.ChampionshipId });
    }
}

public class ChampionshipBetSelectionEntityConfiguration : IEntityTypeConfiguration<ChampionshipBetSelectionEntity>
{
    public void Configure(EntityTypeBuilder<ChampionshipBetSelectionEntity> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.BetId).IsRequired();
        b.Property(x => x.Category).HasConversion<short>().IsRequired();
        b.Property(x => x.PredictedId).IsRequired();
        b.Property(x => x.FichasWagered).IsRequired();
        b.Property(x => x.FichasEarned).IsRequired(false);
        b.Property(x => x.IsCorrect).IsRequired(false);
        b.Property(x => x.ActualIds).IsRequired(false).HasMaxLength(800);

        b.HasIndex(x => new { x.BetId, x.Category }).IsUnique();
    }
}
