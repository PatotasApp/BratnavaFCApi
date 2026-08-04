using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class MonthlyPaymentEntityConfiguration : IEntityTypeConfiguration<MonthlyPaymentEntity>
{
    public void Configure(EntityTypeBuilder<MonthlyPaymentEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.GroupId).IsRequired();
        builder.Property(x => x.PlayerId).IsRequired();
        builder.Property(x => x.Year).IsRequired();
        builder.Property(x => x.Month).IsRequired();
        builder.Property(x => x.Amount).IsRequired().HasColumnType("numeric(10,2)");
        builder.Property(x => x.Discount).IsRequired().HasDefaultValue(0m).HasColumnType("numeric(10,2)");
        builder.Property(x => x.DiscountReason).HasMaxLength(500);
        builder.Property(x => x.Status)
            .HasConversion<short>()
            .HasDefaultValue(PaymentStatus.Pending)
            .IsRequired();

        builder.HasOne(x => x.Player)
            .WithMany()
            .HasForeignKey(x => x.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Um registro por jogador por mês/ano
        builder.HasIndex(x => new { x.GroupId, x.PlayerId, x.Year, x.Month })
            .IsUnique();
    }
}
