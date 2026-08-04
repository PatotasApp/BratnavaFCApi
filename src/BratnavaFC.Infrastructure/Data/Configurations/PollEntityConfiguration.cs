using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class PollEntityConfiguration : IEntityTypeConfiguration<PollEntity>
{
    public void Configure(EntityTypeBuilder<PollEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.GroupId).IsRequired();
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(20).HasDefaultValue("open");
        builder.Property(x => x.DeadlineDate).HasColumnType("date");
        builder.Property(x => x.DeadlineTime).HasColumnType("time without time zone");
        builder.Property(x => x.Type).IsRequired().HasMaxLength(20).HasDefaultValue("poll");
        builder.Property(x => x.EventDate).HasColumnType("date");
        builder.Property(x => x.EventTime).HasColumnType("time without time zone");
        builder.Property(x => x.EventLocation).HasMaxLength(300);
        builder.Property(x => x.EventIcon).HasMaxLength(100);
        builder.Property(x => x.CostType).HasMaxLength(20);
        builder.Property(x => x.CostAmount).HasPrecision(10, 2);
        builder.HasOne<GroupEntity>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Options).WithOne(x => x.Poll).HasForeignKey(x => x.PollId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Votes).WithOne().HasForeignKey(x => x.PollId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.LinkedMatch).WithMany().HasForeignKey(x => x.LinkedMatchId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.GroupId);
    }
}
