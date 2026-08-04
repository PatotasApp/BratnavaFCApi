using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class GroupSettingsEntityConfiguration : IEntityTypeConfiguration<GroupSettingsEntity>
{
    public void Configure(EntityTypeBuilder<GroupSettingsEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.GroupId)
            .IsRequired();

        builder.HasOne<GroupEntity>()
            .WithOne()
            .HasForeignKey<GroupSettingsEntity>(x => x.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.MinPlayers).IsRequired();
        builder.Property(x => x.MaxPlayers).IsRequired();

        builder.Property(x => x.DefaultPlaceName)
            .HasMaxLength(200);

        builder.Property(x => x.DefaultDayOfWeek);
        builder.Property(x => x.DefaultKickoffTime);
        builder.Property(x => x.MatchSchedulingEnabled)
            .HasDefaultValue(false);
        builder.Property(x => x.MatchSchedulingMode)
            .HasDefaultValue((short)0);
        builder.Property(x => x.MatchScheduleDayOfWeek);
        builder.Property(x => x.MatchScheduleTime);
        builder.Property(x => x.ManualMatchSchedulesJson)
            .HasColumnType("text");

        builder.Property(x => x.PaymentMode)
            .HasConversion<short>()
            .HasDefaultValue(PaymentMode.Monthly);

        builder.Property(x => x.MvpTieRule)
            .HasConversion<short>()
            .HasDefaultValue(MvpTieRule.AllMvp);

        builder.Property(x => x.MvpTieMaxPlayers)
            .HasDefaultValue(2);

        builder.Property(x => x.ShowStatsGeneralTab)
            .HasDefaultValue(true);

        builder.Property(x => x.ShowStatsPerMatchTab)
            .HasDefaultValue(true);

        builder.Property(x => x.ShowStatsClassificationTab)
            .HasDefaultValue(true);

        builder.HasIndex(x => x.GroupId).IsUnique();
    }
}
