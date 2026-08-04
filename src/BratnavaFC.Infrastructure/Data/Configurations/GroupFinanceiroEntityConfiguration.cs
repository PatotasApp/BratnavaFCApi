using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class GroupFinanceiroEntityConfiguration : IEntityTypeConfiguration<GroupFinanceiroEntity>
{
    public void Configure(EntityTypeBuilder<GroupFinanceiroEntity> builder)
    {
        builder.HasKey(x => new { x.UserId, x.GroupId });

        builder.HasOne(x => x.Group)
            .WithMany(x => x.Financeiros)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany(x => x.Financeiros)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
