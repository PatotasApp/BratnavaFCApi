using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class PollGuestEntityConfiguration : IEntityTypeConfiguration<PollGuestEntity>
{
    public void Configure(EntityTypeBuilder<PollGuestEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.GuestName).IsRequired();
        builder.HasOne<PollEntity>()
            .WithMany(x => x.Guests)
            .HasForeignKey(x => x.PollId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PlayerEntity>()
            .WithMany()
            .HasForeignKey(x => x.VoterPlayerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.PollId);
        builder.HasIndex(x => x.VoterPlayerId);
    }
}
