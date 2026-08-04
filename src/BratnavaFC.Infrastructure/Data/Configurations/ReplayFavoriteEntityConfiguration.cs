using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class ReplayFavoriteEntityConfiguration : IEntityTypeConfiguration<ReplayFavoriteEntity>
{
    public void Configure(EntityTypeBuilder<ReplayFavoriteEntity> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.ClipId).IsRequired();
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();
        // Um usuário só pode favoritar uma vez por clip
        b.HasIndex(x => new { x.ClipId, x.UserId }).IsUnique();
    }
}
