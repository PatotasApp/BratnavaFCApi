using Microsoft.EntityFrameworkCore;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<MatchEntity> Matches => Set<MatchEntity>();
    public DbSet<MatchPlayerEntity> MatchPlayers => Set<MatchPlayerEntity>();
    public DbSet<VoteEntity> Votes => Set<VoteEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<GroupEntity> Groups => Set<GroupEntity>();
    public DbSet<PlayerEntity> Players => Set<PlayerEntity>();

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

        modelBuilder.Entity<UserEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.FirstName)
                .IsRequired();

            builder.Property(x => x.LastName)
                .IsRequired();

            builder.Property(x => x.Email)
                .IsRequired();

            builder.HasMany(x => x.Players).WithOne(x => x.User).HasForeignKey(x => x.UserId);
            builder.HasMany(x => x.Groups).WithOne(x => x.Admin).HasForeignKey(x => x.AdminId);

            builder.Property(x => x.Status)
                .HasDefaultValue(Status.Active);

            builder.HasQueryFilter(x => x.Status != Status.Inactive);
        });

        modelBuilder.Entity<GroupEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .IsRequired();

            builder.HasMany(x => x.Players).WithOne(x => x.Group).HasForeignKey(x => x.GroupId);

            //builder.HasMany(x => x.Matches).WithOne().HasForeignKey(x => x.GroupId);

            builder.Property(x => x.Status)
                .HasDefaultValue(Status.Active);

            builder.HasQueryFilter(x => x.Status != Status.Inactive);
        });

        modelBuilder.Entity<PlayerEntity>(builder =>
       {
           builder.HasKey(x => x.Id);

           builder.Property(x => x.Name)
                .IsRequired();

           builder.Property(x => x.MainPosition)
                .IsRequired();

           builder.Property(x => x.Positions)
                .IsRequired();

           builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);

           builder.HasOne(x => x.Group).WithMany(x => x.Players).HasForeignKey(x => x.GroupId);

           //builder.HasMany(x => x.Goals).WithOne().HasForeignKey(x => x.PlayerId);

           builder.Property(x => x.Status)
                .HasDefaultValue(Status.Active);
       });
    }
}