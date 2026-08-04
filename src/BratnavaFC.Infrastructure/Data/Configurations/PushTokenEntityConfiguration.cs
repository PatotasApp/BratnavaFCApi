using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class PushTokenEntityConfiguration : IEntityTypeConfiguration<PushTokenEntity>
{
    public void Configure(EntityTypeBuilder<PushTokenEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Token)
            .IsRequired()
            .HasMaxLength(4096);

        builder.Property(x => x.Platform)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Par (UserId, Token) único: permite que o mesmo dispositivo tenha
        // registros para usuários diferentes sem roubar o token de ninguém.
        builder.HasIndex(x => new { x.UserId, x.Token }).IsUnique();
    }
}
