using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class GroupAdminEntityConfiguration : IEntityTypeConfiguration<GroupAdminEntity>
{
    public void Configure(EntityTypeBuilder<GroupAdminEntity> builder)
    {
        builder.HasKey(x => new { x.UserId, x.GroupId });

        builder.HasOne(x => x.Group)
            .WithMany(x => x.Admins)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany(x => x.Admins)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
