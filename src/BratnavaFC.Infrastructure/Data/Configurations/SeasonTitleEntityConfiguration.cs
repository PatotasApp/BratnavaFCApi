using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public sealed class SeasonTitleEntityConfiguration : IEntityTypeConfiguration<SeasonTitleEntity>
{
    public void Configure(EntityTypeBuilder<SeasonTitleEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Category).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Icon).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Scope).HasDefaultValue(SeasonTitleEntity.ScopeSeason);
        // Título da temporada: um por temporada/categoria/jogador. Do ano: um por ano/categoria/jogador.
        builder.HasIndex(x => new { x.GroupId, x.SeasonId, x.Category, x.PlayerId }).IsUnique()
            .HasFilter("\"Scope\" = 1").HasDatabaseName("IX_SeasonTitles_Season");
        builder.HasIndex(x => new { x.GroupId, x.Season, x.Category, x.PlayerId }).IsUnique()
            .HasFilter("\"Scope\" = 2").HasDatabaseName("IX_SeasonTitles_Year");
        builder.HasOne<SeasonEntity>().WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PlayerEntity>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
