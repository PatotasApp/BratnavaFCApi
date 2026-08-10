using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserName).IsRequired();
        builder.Property(x => x.FirstName).IsRequired();
        builder.Property(x => x.LastName).IsRequired();
        builder.Property(x => x.Email).IsRequired();
        builder.Property(x => x.ProfilePhotoData).HasColumnType("bytea");
        builder.Property(x => x.ProfilePhotoContentType).HasMaxLength(32);
        builder.Property(x => x.ProfilePhotoUpdatedAt);
        builder.Property(x => x.ProfileVisibility)
            .HasConversion<short>()
            .HasDefaultValue(ProfileVisibility.AuthenticatedUsers)
            .IsRequired();
        builder.Property(x => x.ShowPatotaNamesOnProfile).HasDefaultValue(true).IsRequired();
        builder.Property(x => x.ShowZoeiraAchievementsOnProfile).HasDefaultValue(false).IsRequired();

        builder.HasMany(x => x.Players)
            .WithOne(x => x.User)
            .HasForeignKey(x => x.UserId);

        builder.Property(x => x.Status)
            .HasConversion<short>()
            .HasDefaultValue(Status.Active)
            .IsRequired();

        builder.Property(x => x.InactivatedAt);
    }
}
