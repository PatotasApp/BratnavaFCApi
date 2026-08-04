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
