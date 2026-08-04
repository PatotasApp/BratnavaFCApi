using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class ScheduledNotificationJobEntityConfiguration : IEntityTypeConfiguration<ScheduledNotificationJobEntity>
{
    public void Configure(EntityTypeBuilder<ScheduledNotificationJobEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EntityType).IsRequired().HasMaxLength(20);
        builder.Property(x => x.TriggerType).IsRequired().HasMaxLength(10);
        builder.Property(x => x.HangfireJobId).IsRequired().HasMaxLength(100);
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
