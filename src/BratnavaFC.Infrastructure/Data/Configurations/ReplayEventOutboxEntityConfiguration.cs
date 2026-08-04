using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class ReplayEventOutboxEntityConfiguration : IEntityTypeConfiguration<ReplayEventOutboxEntity>
{
    public void Configure(EntityTypeBuilder<ReplayEventOutboxEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StreamKey).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Type).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.Payload).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.StreamFieldsJson).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.RedisStreamEntryId).HasMaxLength(100);
        builder.HasIndex(x => new { x.Status, x.CreateDate });
        builder.HasIndex(x => new { x.GroupId, x.MatchId, x.CreateDate });
    }
}
