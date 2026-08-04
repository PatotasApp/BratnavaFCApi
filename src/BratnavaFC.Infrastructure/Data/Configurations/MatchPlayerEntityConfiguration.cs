using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class MatchPlayerEntityConfiguration : IEntityTypeConfiguration<MatchPlayerEntity>
{
    public void Configure(EntityTypeBuilder<MatchPlayerEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.InviteResponse)
            .HasDefaultValue(InviteResponse.None);

        builder.Property(x => x.GroupId)
            .IsRequired();

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Team)
            .HasDefaultValue((short)0)
            .IsRequired();

        builder.HasOne(x => x.Match)
            .WithMany(x => x.Players)
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Player)
            .WithMany(x => x.MatchPlayers)
            .HasForeignKey(x => x.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.ReceivedVotes)
            .WithOne(x => x.VotedFor)
            .HasForeignKey(x => x.VotedForId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.GoalsScored)
            .WithOne(g => g.ScorerMatchPlayer)
            .HasForeignKey(g => g.ScorerMatchPlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.GoalsAssisted)
            .WithOne(g => g.AssistMatchPlayer)
            .HasForeignKey(g => g.AssistMatchPlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.MatchId, x.PlayerId })
            .IsUnique();

        builder.HasIndex(x => new { x.MatchId, x.PlayerId, x.GroupId })
            .IsUnique();

        builder.Property(x => x.AutoRejectedByAbsenceId).IsRequired(false);

        builder.HasOne(x => x.AutoRejectedByAbsence)
            .WithMany()
            .HasForeignKey(x => x.AutoRejectedByAbsenceId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
