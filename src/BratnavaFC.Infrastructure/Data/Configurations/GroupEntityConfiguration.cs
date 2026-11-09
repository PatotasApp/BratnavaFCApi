using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class GroupEntityConfiguration : IEntityTypeConfiguration<GroupEntity>
{
    public void Configure(EntityTypeBuilder<GroupEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(8).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.IsPublic).HasDefaultValue(true).IsRequired();
        builder.Property(x => x.IsAcceptingPlayers).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ShowMembersOnProfile).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ProfileDescription).HasMaxLength(500);
        builder.Property(x => x.ProfileCity).HasMaxLength(100);

        builder.Property(x => x.CreatedByUserId);
        builder.Property(x => x.LogoKey).HasMaxLength(200);
        builder.Property(x => x.LogoUpdatedAt);

        builder.HasMany(x => x.Players)
            .WithOne(x => x.Group)
            .HasForeignKey(x => x.GroupId);

        builder.Property(x => x.Status)
            .HasConversion<short>()
            .HasDefaultValue(Status.Active)
            .IsRequired();

        builder.Property(x => x.InactivatedAt);
    }
}
