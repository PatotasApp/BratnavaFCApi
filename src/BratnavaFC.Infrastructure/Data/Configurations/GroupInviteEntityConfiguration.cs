using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class GroupInviteEntityConfiguration : IEntityTypeConfiguration<GroupInviteEntity>
{
    public void Configure(EntityTypeBuilder<GroupInviteEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<short>()
            .HasDefaultValue(GroupInviteStatus.Pending)
            .IsRequired();

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.TargetUser)
            .WithMany()
            .HasForeignKey(x => x.TargetUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.GuestPlayer)
            .WithMany()
            .HasForeignKey(x => x.GuestPlayerId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // índice para buscar convites pendentes de um usuário
        builder.HasIndex(x => new { x.TargetUserId, x.Status });
        // evitar convites duplicados pendentes para o mesmo par usuário+grupo
        builder.HasIndex(x => new { x.GroupId, x.TargetUserId, x.Status });
    }
}
