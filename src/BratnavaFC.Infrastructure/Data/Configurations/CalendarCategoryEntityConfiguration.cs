using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class CalendarCategoryEntityConfiguration : IEntityTypeConfiguration<CalendarCategoryEntity>
{
    public void Configure(EntityTypeBuilder<CalendarCategoryEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.GroupId).IsRequired();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Color).HasMaxLength(20);
        builder.Property(x => x.Icon).HasMaxLength(100);
        builder.Property(x => x.IsSystem).IsRequired().HasDefaultValue(false);

        builder.HasOne<GroupEntity>()
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.GroupId);
    }
}
