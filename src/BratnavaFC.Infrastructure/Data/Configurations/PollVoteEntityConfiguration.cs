using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class PollVoteEntityConfiguration : IEntityTypeConfiguration<PollVoteEntity>
{
    public void Configure(EntityTypeBuilder<PollVoteEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.PollId, x.PlayerId });
        builder.HasOne(x => x.Option).WithMany().HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.Cascade);
    }
}
