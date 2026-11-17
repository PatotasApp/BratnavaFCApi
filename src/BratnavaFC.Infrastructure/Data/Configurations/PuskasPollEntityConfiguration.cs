using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public sealed class PuskasPollEntityConfiguration : IEntityTypeConfiguration<PuskasPollEntity>
{
    public void Configure(EntityTypeBuilder<PuskasPollEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ClipIdsJson).IsRequired();
        builder.HasIndex(x => x.GroupId).IsUnique();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class PuskasVoteEntityConfiguration : IEntityTypeConfiguration<PuskasVoteEntity>
{
    public void Configure(EntityTypeBuilder<PuskasVoteEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.VoterName).HasMaxLength(80).IsRequired();
        builder.Property(x => x.VoterKey).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => new { x.PollId, x.VoterKey, x.ClipId }).IsUnique();
        builder.HasIndex(x => x.PollId);
        builder.HasOne<PuskasPollEntity>().WithMany().HasForeignKey(x => x.PollId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ReplayClipEntity>().WithMany().HasForeignKey(x => x.ClipId).OnDelete(DeleteBehavior.Cascade);
    }
}
