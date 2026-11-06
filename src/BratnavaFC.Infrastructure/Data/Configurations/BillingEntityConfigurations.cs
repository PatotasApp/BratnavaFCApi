using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Entities.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class BillingSettingsEntityConfiguration : IEntityTypeConfiguration<BillingSettingsEntity>
{
    public void Configure(EntityTypeBuilder<BillingSettingsEntity> builder)
    {
        builder.ToTable("BillingSettings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ReminderDays).IsRequired().HasMaxLength(60);
    }
}

public class BillingProductEntityConfiguration : IEntityTypeConfiguration<BillingProductEntity>
{
    public void Configure(EntityTypeBuilder<BillingProductEntity> builder)
    {
        builder.ToTable("BillingProducts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProductId).IsRequired().HasMaxLength(150);
        builder.HasIndex(x => new { x.Store, x.ProductId }).IsUnique();
        // Um só produto ativo por loja e duração.
        builder.HasIndex(x => new { x.Store, x.Months })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
    }
}

public class GroupBillingEntityConfiguration : IEntityTypeConfiguration<GroupBillingEntity>
{
    public void Configure(EntityTypeBuilder<GroupBillingEntity> builder)
    {
        builder.ToTable("GroupBillings");
        builder.HasKey(x => x.GroupId);
        builder.Ignore(x => x.AccessEndsAt);
        builder.Property(x => x.LifetimeReason).HasMaxLength(300);
        builder.Property(x => x.LastReminderKey).HasMaxLength(40);
        builder.HasOne<GroupEntity>()
            .WithOne()
            .HasForeignKey<GroupBillingEntity>(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class BillingTrialGrantEntityConfiguration : IEntityTypeConfiguration<BillingTrialGrantEntity>
{
    public void Configure(EntityTypeBuilder<BillingTrialGrantEntity> builder)
    {
        // Sem FK de propósito: precisa sobreviver à exclusão da patota e da conta.
        builder.ToTable("BillingTrialGrants");
        builder.HasKey(x => x.UserId);
        builder.HasIndex(x => x.GroupId);
    }
}

public class BillingPurchaseEntityConfiguration : IEntityTypeConfiguration<BillingPurchaseEntity>
{
    public void Configure(EntityTypeBuilder<BillingPurchaseEntity> builder)
    {
        // Sem FK para Groups: o histórico de cobrança fica mesmo se a patota for apagada
        // (receita, reembolsos, e a loja continua mandando avisos).
        builder.ToTable("BillingPurchases");
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.NeedsConsume);
        builder.Property(x => x.ProductId).IsRequired().HasMaxLength(150);
        builder.Property(x => x.TransactionId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.PurchaseToken).HasMaxLength(500);
        builder.Property(x => x.Currency).HasMaxLength(3);
        builder.Property(x => x.Environment).IsRequired().HasMaxLength(20);
        builder.Property(x => x.RefundReason).HasMaxLength(200);
        // A garantia de "não somar o mesmo pacote duas vezes".
        builder.HasIndex(x => new { x.Store, x.TransactionId }).IsUnique();
        builder.HasIndex(x => new { x.GroupId, x.PurchasedAt });
        builder.HasIndex(x => x.PurchaseToken);
    }
}

public class BillingAdjustmentEntityConfiguration : IEntityTypeConfiguration<BillingAdjustmentEntity>
{
    public void Configure(EntityTypeBuilder<BillingAdjustmentEntity> builder)
    {
        // Sem FK: a auditoria fica mesmo se a patota for apagada.
        builder.ToTable("BillingAdjustments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(BillingAdjustmentEntity.ReasonMaxLength);
        builder.Property(x => x.Details).HasMaxLength(2000);
        builder.HasIndex(x => new { x.GroupId, x.CreateDate });
    }
}

public class StoreEventEntityConfiguration : IEntityTypeConfiguration<StoreEventEntity>
{
    public void Configure(EntityTypeBuilder<StoreEventEntity> builder)
    {
        builder.ToTable("StoreEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExternalId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.NotificationType).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Subtype).HasMaxLength(80);
        builder.Property(x => x.RawPayload).IsRequired();
        builder.Property(x => x.Error).HasMaxLength(1000);
        builder.Property(x => x.StoreReference).HasMaxLength(500);
        builder.HasIndex(x => new { x.Store, x.ExternalId }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreateDate });
        builder.HasIndex(x => x.GroupId);
    }
}
