using Microsoft.EntityFrameworkCore;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<MatchEntity> Matches => Set<MatchEntity>();
    public DbSet<MatchPlayerEntity> MatchPlayers => Set<MatchPlayerEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MatchEntity>(b =>
        {
            b.HasKey(m => m.Id);
            b.HasMany(m => m.Players)
             .WithOne(p => p.Match)
             .HasForeignKey(p => p.Id)
             .OnDelete(DeleteBehavior.Cascade);

            b.Ignore(x => x.TeamAPlayers);
            b.Ignore(x => x.TeamBPlayers);
        });

        modelBuilder.Entity<MatchPlayerEntity>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).IsRequired();
        });
    }
}