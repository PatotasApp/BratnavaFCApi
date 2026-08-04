using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class ReplayClipEntityConfiguration : IEntityTypeConfiguration<ReplayClipEntity>
{
    public void Configure(EntityTypeBuilder<ReplayClipEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.GroupId).IsRequired();
        builder.Property(x => x.MatchId).IsRequired();

        builder.Property(x => x.BucketName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.ObjectKey)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.ETag)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.RecordedAt).IsRequired();

        builder.Property(x => x.EventType)
            .HasConversion<short>()
            .IsRequired();

        // Um mesmo arquivo não pode ser registrado duas vezes
        builder.HasIndex(x => x.ObjectKey).IsUnique();

        // Busca de clips por partida
        builder.HasIndex(x => new { x.MatchId, x.EventType });
    }
}
