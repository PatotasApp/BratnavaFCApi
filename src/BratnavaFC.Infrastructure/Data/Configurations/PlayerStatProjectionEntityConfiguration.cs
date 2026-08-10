using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public sealed class PlayerStatProjectionEntityConfiguration : IEntityTypeConfiguration<PlayerStatProjectionEntity>
{
    public void Configure(EntityTypeBuilder<PlayerStatProjectionEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.WinRatePct);
        builder.HasIndex(x => new { x.GroupId, x.PlayerId, x.Season }).IsUnique();
        builder.HasOne<PlayerEntity>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
