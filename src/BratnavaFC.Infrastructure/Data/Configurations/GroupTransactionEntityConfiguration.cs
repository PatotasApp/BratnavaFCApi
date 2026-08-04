using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class GroupTransactionEntityConfiguration : IEntityTypeConfiguration<GroupTransactionEntity>
{
    public void Configure(EntityTypeBuilder<GroupTransactionEntity> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.GroupId).IsRequired();
        b.Property(x => x.Amount).IsRequired().HasColumnType("numeric(10,2)");
        b.Property(x => x.Description).IsRequired().HasMaxLength(500);
        b.Property(x => x.Date).IsRequired().HasColumnType("date");
        b.Property(x => x.Type).HasConversion<short>().IsRequired();
        b.Property(x => x.SourceType).HasConversion<short>().IsRequired();
        b.Property(x => x.Category).HasConversion<short>().IsRequired(false);
        b.Property(x => x.IsAutomatic).IsRequired().HasDefaultValue(false);
        b.Property(x => x.PlayerName).HasMaxLength(200);
        b.Property(x => x.SourceId).IsRequired(false);
        b.Property(x => x.CreatedByUserId).IsRequired(false);

        b.HasOne<GroupEntity>()
         .WithMany()
         .HasForeignKey(x => x.GroupId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.GroupId, x.Date });
        b.HasIndex(x => new { x.SourceType, x.SourceId });
    }
}
