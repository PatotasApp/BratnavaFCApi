using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class VoteEntityConfiguration : IEntityTypeConfiguration<VoteEntity>
{
    public void Configure(EntityTypeBuilder<VoteEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Match)
            .WithMany(x => x.Votes)
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Voter)
            .WithMany()
            .HasForeignKey(x => x.VoterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.VotedFor)
            .WithMany(x => x.ReceivedVotes)
            .HasForeignKey(x => x.VotedForId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.MatchId, x.VoterId })
            .IsUnique();
    }
}
