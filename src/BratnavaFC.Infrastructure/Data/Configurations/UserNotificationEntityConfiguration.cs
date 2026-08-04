using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class UserNotificationEntityConfiguration : IEntityTypeConfiguration<UserNotificationEntity>
{
    public void Configure(EntityTypeBuilder<UserNotificationEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Body).IsRequired().HasMaxLength(500);
        builder.Property(x => x.Type).HasMaxLength(60);
        builder.HasIndex(x => new { x.UserId, x.IsRead, x.CreateDate });
    }
}
