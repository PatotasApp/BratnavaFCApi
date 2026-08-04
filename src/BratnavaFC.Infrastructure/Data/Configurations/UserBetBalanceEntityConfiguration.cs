using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class UserBetBalanceEntityConfiguration : IEntityTypeConfiguration<UserBetBalanceEntity>
{
    public void Configure(EntityTypeBuilder<UserBetBalanceEntity> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.GroupId).IsRequired();
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.Balance).IsRequired().HasDefaultValue(0);
        b.Property(x => x.TotalBets).IsRequired().HasDefaultValue(0);
        b.Property(x => x.TotalCorrect).IsRequired().HasDefaultValue(0);

        // Um saldo por usuário por grupo
        b.HasIndex(x => new { x.GroupId, x.UserId }).IsUnique();
    }
}
