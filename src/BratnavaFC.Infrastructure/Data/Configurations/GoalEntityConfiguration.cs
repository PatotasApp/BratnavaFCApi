using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class GoalEntityConfiguration : IEntityTypeConfiguration<GoalEntity>
{
    public void Configure(EntityTypeBuilder<GoalEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.MatchId).IsRequired();
        builder.Property(x => x.GroupId).IsRequired();

        builder.Property(x => x.ScorerMatchPlayerId).IsRequired();
        builder.Property(x => x.AssistMatchPlayerId).IsRequired(false);

        builder.Property(x => x.TimeSeconds).IsRequired(false);

        builder.Property(x => x.IsOwnGoal).IsRequired().HasDefaultValue(false);

        builder.HasOne(x => x.Match)
            .WithMany(m => m.Goals)
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ScorerMatchPlayer)
            .WithMany(mp => mp.GoalsScored)
            .HasForeignKey(x => x.ScorerMatchPlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AssistMatchPlayer)
            .WithMany(mp => mp.GoalsAssisted)
            .HasForeignKey(x => x.AssistMatchPlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.MatchId, x.ScorerMatchPlayerId });
        builder.HasIndex(x => new { x.MatchId, x.AssistMatchPlayerId });
    }
}
