using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class MatchEntityConfiguration : IEntityTypeConfiguration<MatchEntity>
{
    public void Configure(EntityTypeBuilder<MatchEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.GroupId).IsRequired();

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Players)
            .WithOne(x => x.Match)
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Votes)
            .WithOne(x => x.Match)
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(x => x.TeamAPlayers);
        builder.Ignore(x => x.TeamBPlayers);

        builder.HasOne(x => x.TeamAColor)
            .WithMany()
            .HasForeignKey(x => x.TeamAColorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.TeamBColor)
            .WithMany()
            .HasForeignKey(x => x.TeamBColorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.LinkedPoll)
            .WithMany()
            .HasForeignKey(x => x.LinkedPollId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(x => x.Status)
            .HasConversion<short>()
            .HasDefaultValue(MatchStatus.Created)
            .IsRequired();

        builder.HasMany(x => x.Goals)
            .WithOne(x => x.Match)
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.GroupId, x.PlayedAt });
    }
}
