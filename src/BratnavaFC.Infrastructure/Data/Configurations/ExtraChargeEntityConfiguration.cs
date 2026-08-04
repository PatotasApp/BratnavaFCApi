using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class ExtraChargeEntityConfiguration : IEntityTypeConfiguration<ExtraChargeEntity>
{
    public void Configure(EntityTypeBuilder<ExtraChargeEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.GroupId).IsRequired();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Amount).IsRequired().HasColumnType("numeric(10,2)");
        builder.Property(x => x.IsCancelled).IsRequired().HasDefaultValue(false);

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Payments)
            .WithOne(x => x.ExtraCharge)
            .HasForeignKey(x => x.ExtraChargeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.GroupId);
    }
}
