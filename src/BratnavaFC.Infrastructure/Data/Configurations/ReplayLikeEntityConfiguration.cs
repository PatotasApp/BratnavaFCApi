using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class ReplayLikeEntityConfiguration : IEntityTypeConfiguration<ReplayLikeEntity>
{
    public void Configure(EntityTypeBuilder<ReplayLikeEntity> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.ClipId).IsRequired();
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();
        // Um usuário só pode dar like uma vez por clip
        b.HasIndex(x => new { x.ClipId, x.UserId }).IsUnique();
    }
}
