using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class CalendarEventEntityConfiguration : IEntityTypeConfiguration<CalendarEventEntity>
{
    public void Configure(EntityTypeBuilder<CalendarEventEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.GroupId).IsRequired();
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.TimeTBD).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.Icon).HasMaxLength(100);

        builder.HasOne<GroupEntity>()
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.GroupId, x.EventDate });
    }
}
