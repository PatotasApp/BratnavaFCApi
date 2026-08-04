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

        builder.Property(x => x.CreatedByUserId).IsRequired();

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
