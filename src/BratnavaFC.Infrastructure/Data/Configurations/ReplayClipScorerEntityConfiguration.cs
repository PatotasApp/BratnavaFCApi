using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public sealed class ReplayClipScorerEntityConfiguration : IEntityTypeConfiguration<ReplayClipScorerEntity>
{
    public void Configure(EntityTypeBuilder<ReplayClipScorerEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ClipId).IsUnique();
        builder.HasIndex(x => x.GroupId);
        builder.HasOne<ReplayClipEntity>().WithMany().HasForeignKey(x => x.ClipId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PlayerEntity>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
