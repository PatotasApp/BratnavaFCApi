using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public sealed class MatchApplicationEntityConfiguration : IEntityTypeConfiguration<MatchApplicationEntity>
{
    public void Configure(EntityTypeBuilder<MatchApplicationEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<short>().IsRequired();
        builder.Property(x => x.Source).HasConversion<short>().IsRequired();
        builder.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.GuestPlayer).WithMany().HasForeignKey(x => x.GuestPlayerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Opening).WithMany().HasForeignKey(x => x.OpeningId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.MatchId, x.UserId }).IsUnique()
            .HasFilter("\"Status\" IN (1, 2, 3)");
        builder.HasIndex(x => new { x.GroupId, x.Status });
        builder.HasIndex(x => new { x.UserId, x.Status });
    }
}

public sealed class MatchOpeningEntityConfiguration : IEntityTypeConfiguration<MatchOpeningEntity>
{
    public void Configure(EntityTypeBuilder<MatchOpeningEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Position).HasConversion<short>().IsRequired();
        builder.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.MatchId, x.IsActive });
    }
}
