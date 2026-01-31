using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<MatchEntity> Matches => Set<MatchEntity>();
    public DbSet<MatchPlayerEntity> MatchPlayers => Set<MatchPlayerEntity>();
    public DbSet<VoteEntity> Votes => Set<VoteEntity>();
    public DbSet<TeamColorEntity> TeamColors => Set<TeamColorEntity>();

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

            // optional relationship to team colors
            b.HasOne(m => m.TeamAColor)
             .WithMany()
             .HasForeignKey(m => m.TeamAColorId)
             .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(m => m.TeamBColor)
             .WithMany()
             .HasForeignKey(m => m.TeamBColorId)
             .OnDelete(DeleteBehavior.SetNull);
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

        modelBuilder.Entity<TeamColorEntity>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).IsRequired().HasMaxLength(100);
            b.Property(c => c.HexValue).IsRequired().HasMaxLength(10);
        });
    }

    public override int SaveChanges()
    {
        ApplyTimestamps();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyTimestamps()
    {
        var utcNow = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(BaseEntity.CreateDate)).CurrentValue = utcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(BaseEntity.CreateDate)).IsModified = false;
                entry.Property(nameof(BaseEntity.UpdateDate)).CurrentValue = utcNow;
            }
        }
    }
}