using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public sealed class SeasonEntityConfiguration : IEntityTypeConfiguration<SeasonEntity>
{
    public void Configure(EntityTypeBuilder<SeasonEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(SeasonEntity.NameMaxLength);
        builder.Ignore(x => x.IsOpen);

        builder.HasIndex(x => new { x.GroupId, x.Year, x.Number }).IsUnique();
        builder.HasIndex(x => new { x.GroupId, x.StartsAt }).IsUnique();
        // Só uma temporada aberta por patota.
        builder.HasIndex(x => x.GroupId).IsUnique().HasFilter("\"EndsAt\" IS NULL")
            .HasDatabaseName("IX_Seasons_GroupId_Open");

        builder.HasOne<GroupEntity>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}
