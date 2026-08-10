using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public sealed class PlayerMatchStatContributionEntityConfiguration : IEntityTypeConfiguration<PlayerMatchStatContributionEntity>
{
    public void Configure(EntityTypeBuilder<PlayerMatchStatContributionEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Result).HasMaxLength(1).IsRequired();
        builder.HasIndex(x => new { x.MatchId, x.PlayerId }).IsUnique();
        builder.HasIndex(x => new { x.GroupId, x.PlayerId, x.Season });
        builder.HasOne<MatchEntity>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PlayerEntity>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
