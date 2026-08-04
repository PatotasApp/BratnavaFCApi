using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class TeamColorEntityConfiguration : IEntityTypeConfiguration<TeamColorEntity>
{
    public void Configure(EntityTypeBuilder<TeamColorEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.GroupId)
            .IsRequired();

        builder.HasOne<GroupEntity>()
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.HexValue)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.HasIndex(x => new { x.GroupId, x.Name }).IsUnique(false);
    }
}
