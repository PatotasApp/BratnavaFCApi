using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class UserAbsenceEntityConfiguration : IEntityTypeConfiguration<UserAbsenceEntity>
{
    public void Configure(EntityTypeBuilder<UserAbsenceEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.StartDate).IsRequired().HasColumnType("date");
        builder.Property(x => x.EndDate).IsRequired().HasColumnType("date");
        builder.Property(x => x.AbsenceType)
            .HasConversion<short>()
            .IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.UserId, x.StartDate, x.EndDate });
    }
}
