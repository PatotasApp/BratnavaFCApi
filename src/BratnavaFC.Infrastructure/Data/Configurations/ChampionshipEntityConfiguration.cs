using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public sealed class ChampionshipEntityConfiguration : IEntityTypeConfiguration<ChampionshipEntity>
{
    public void Configure(EntityTypeBuilder<ChampionshipEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.GroupId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PlaceName).IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<short>()
            .HasDefaultValue(ChampionshipStatus.Created)
            .IsRequired();

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        // Jogos do campeonato: Restrict para nunca apagar partidas (e estatísticas) em cascata por engano.
        builder.HasMany(x => x.Matches)
            .WithOne(x => x.Championship)
            .HasForeignKey(x => x.ChampionshipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.GroupId, x.PlayedAt });
    }
}
