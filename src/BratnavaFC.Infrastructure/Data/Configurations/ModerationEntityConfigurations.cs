using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class ContentReportEntityConfiguration : IEntityTypeConfiguration<ContentReportEntity>
{
    public void Configure(EntityTypeBuilder<ContentReportEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(ContentReportEntity.ReasonMaxLength);
        builder.Property(x => x.Details).HasMaxLength(ContentReportEntity.DetailsMaxLength);
        builder.Property(x => x.ResolutionNote).HasMaxLength(ContentReportEntity.DetailsMaxLength);
        builder.HasIndex(x => new { x.IsResolved, x.CreateDate });
        builder.HasIndex(x => new { x.ReporterUserId, x.TargetType, x.TargetId });
    }
}

public class UserBlockEntityConfiguration : IEntityTypeConfiguration<UserBlockEntity>
{
    public void Configure(EntityTypeBuilder<UserBlockEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.BlockerUserId, x.BlockedUserId }).IsUnique();
        builder.HasIndex(x => x.BlockedUserId);
    }
}
