using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class MatchBetSelectionEntityConfiguration : IEntityTypeConfiguration<MatchBetSelectionEntity>
{
    public void Configure(EntityTypeBuilder<MatchBetSelectionEntity> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.BetId).IsRequired();
        b.Property(x => x.Category).HasConversion<short>().IsRequired();
        b.Property(x => x.PredictedValue).IsRequired().HasMaxLength(200);
        b.Property(x => x.FichasWagered).IsRequired();
        b.Property(x => x.FichasEarned).IsRequired(false);
        b.Property(x => x.IsCorrect).IsRequired(false);
        b.Property(x => x.IsPartialCredit).IsRequired(false);
        b.Property(x => x.ActualValue).IsRequired(false).HasMaxLength(200);

        b.HasIndex(x => x.BetId);
    }
}
