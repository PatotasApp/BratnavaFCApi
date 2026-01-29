using Microsoft.EntityFrameworkCore;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<MatchEntity> Matches => Set<MatchEntity>();
    public DbSet<MatchPlayerEntity> MatchPlayers => Set<MatchPlayerEntity>();
    public DbSet<VoteEntity> Votes => Set<VoteEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MatchEntity>(b =>
        {
            b.HasKey(m => m.Id);
            b.HasMany(m => m.Players)
             .WithOne(p => p.Match)
             .HasForeignKey(p => p.MatchId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(m => m.Votes)
             .WithOne(v => v.Match)
             .HasForeignKey(v => v.MatchId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Ignore(x => x.TeamAPlayers);
            b.Ignore(x => x.TeamBPlayers);
        });

        modelBuilder.Entity<MatchPlayerEntity>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).IsRequired();

            b.HasMany(p => p.ReceivedVotes)
             .WithOne(v => v.VotedFor)
             .HasForeignKey(v => v.VotedForId)
             .OnDelete(DeleteBehavior.Restrict);

            b.Property(p => p.VotedForId).IsRequired(false);
        });

        modelBuilder.Entity<VoteEntity>(b =>
        {
            b.HasKey(v => v.Id);

            b.HasOne(v => v.Match)
             .WithMany(m => m.Votes)
             .HasForeignKey(v => v.MatchId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(v => v.Voter)
             .WithMany() 
             .HasForeignKey(v => v.VoterId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(v => v.VotedFor)
             .WithMany(p => p.ReceivedVotes)
             .HasForeignKey(v => v.VotedForId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(v => new { v.MatchId, v.VoterId }).IsUnique();
        });
    }
}