using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class ExtraChargePaymentEntityConfiguration : IEntityTypeConfiguration<ExtraChargePaymentEntity>
{
    public void Configure(EntityTypeBuilder<ExtraChargePaymentEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExtraChargeId).IsRequired();
        builder.Property(x => x.PlayerId).IsRequired();
        builder.Property(x => x.GroupId).IsRequired();
        builder.Property(x => x.Amount).IsRequired().HasColumnType("numeric(10,2)");
        builder.Property(x => x.Discount).IsRequired().HasDefaultValue(0m).HasColumnType("numeric(10,2)");
        builder.Property(x => x.DiscountReason).HasMaxLength(500);
        builder.Property(x => x.Status)
            .HasConversion<short>()
            .HasDefaultValue(PaymentStatus.Pending)
            .IsRequired();

        builder.Ignore(x => x.FinalAmount);

        builder.HasOne(x => x.ExtraCharge)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.ExtraChargeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Player)
            .WithMany()
            .HasForeignKey(x => x.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Um pagamento por jogador por cobrança
        builder.HasIndex(x => new { x.ExtraChargeId, x.PlayerId })
            .IsUnique();
    }
}
