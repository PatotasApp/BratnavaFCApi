using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class PollOptionImageEntityConfiguration : IEntityTypeConfiguration<PollOptionImageEntity>
{
    public void Configure(EntityTypeBuilder<PollOptionImageEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ImageUrl).IsRequired().HasColumnType("text");
        builder.HasIndex(x => x.OptionId);
    }
}
