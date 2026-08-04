using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class PollOptionEntityConfiguration : IEntityTypeConfiguration<PollOptionEntity>
{
    public void Configure(EntityTypeBuilder<PollOptionEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PollId).IsRequired();
        builder.Property(x => x.Text).IsRequired().HasMaxLength(300);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.PollId);
        builder.HasMany(x => x.Images).WithOne(x => x.Option).HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.Cascade);
    }
}
