using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public sealed class ParticipationActionHistoryEntityConfiguration
    : IEntityTypeConfiguration<ParticipationActionHistoryEntity>
{
    public void Configure(EntityTypeBuilder<ParticipationActionHistoryEntity> builder)
    {
        builder.ToTable("ParticipationActionHistory");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SubjectType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Details).HasMaxLength(1000);
        builder.HasIndex(x => new { x.GroupId, x.SubjectType, x.SubjectId, x.PlayerId, x.CreateDate });
    }
}
