using System;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<MatchEntity> Matches => Set<MatchEntity>();
    public DbSet<MatchPlayerEntity> MatchPlayers => Set<MatchPlayerEntity>();
    public DbSet<VoteEntity> Votes => Set<VoteEntity>();
    public DbSet<TeamColorEntity> TeamColors => Set<TeamColorEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<GroupEntity> Groups => Set<GroupEntity>();
    public DbSet<GroupAdminEntity> GroupAdmins => Set<GroupAdminEntity>();
    public DbSet<PlayerEntity> Players => Set<PlayerEntity>();
    public DbSet<GroupSettingsEntity> GroupSettings => Set<GroupSettingsEntity>();
    public DbSet<GoalEntity> Goals => Set<GoalEntity>();
    public DbSet<GroupInviteEntity> GroupInvites => Set<GroupInviteEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MatchEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.GroupId).IsRequired();

            builder.HasOne(x => x.Group)
                .WithMany()
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(x => x.Players)
                .WithOne(x => x.Match)
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(x => x.Votes)
                .WithOne(x => x.Match)
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Ignore(x => x.TeamAPlayers);
            builder.Ignore(x => x.TeamBPlayers);

            builder.HasOne(x => x.TeamAColor)
                .WithMany()
                .HasForeignKey(x => x.TeamAColorId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(x => x.TeamBColor)
                .WithMany()
                .HasForeignKey(x => x.TeamBColorId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Property(x => x.Status)
                .HasConversion<short>()
                .HasDefaultValue(MatchStatus.Created)
                .IsRequired();

            builder.HasMany(x => x.Goals)
                .WithOne(x => x.Match)
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);


            builder.HasIndex(x => new { x.GroupId, x.PlayedAt });
        });

        modelBuilder.Entity<MatchPlayerEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.InviteResponse)
                .HasDefaultValue(InviteResponse.None);

            builder.Property(x => x.GroupId)
                .IsRequired();

            builder.HasOne(x => x.Group)
                .WithMany()
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Property(x => x.Team)
                .HasDefaultValue((short)0)
                .IsRequired();

            builder.HasOne(x => x.Match)
                .WithMany(x => x.Players)
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Player)
                .WithMany(x => x.MatchPlayers)
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(x => x.ReceivedVotes)
                .WithOne(x => x.VotedFor)
                .HasForeignKey(x => x.VotedForId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(x => x.GoalsScored)
                .WithOne(g => g.ScorerMatchPlayer)
                .HasForeignKey(g => g.ScorerMatchPlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(x => x.GoalsAssisted)
                .WithOne(g => g.AssistMatchPlayer)
                .HasForeignKey(g => g.AssistMatchPlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => new { x.MatchId, x.PlayerId })
                .IsUnique();

            builder.HasIndex(x => new { x.MatchId, x.PlayerId, x.GroupId })
                .IsUnique();
        });

        modelBuilder.Entity<VoteEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.HasOne(x => x.Match)
                .WithMany(x => x.Votes)
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Voter)
                .WithMany()
                .HasForeignKey(x => x.VoterId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.VotedFor)
                .WithMany(x => x.ReceivedVotes)
                .HasForeignKey(x => x.VotedForId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => new { x.MatchId, x.VoterId })
                .IsUnique();
        });

        modelBuilder.Entity<PlayerEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name).IsRequired();
            builder.Property(x => x.IsGoalkeeper).IsRequired();
            builder.Property(x => x.IsGuest).IsRequired().HasDefaultValue(false);

            builder.HasOne(x => x.User)
                .WithMany(x => x.Players)
                .HasForeignKey(x => x.UserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(x => x.Group)
                .WithMany(x => x.Players)
                .HasForeignKey(x => x.GroupId);

            builder.HasMany(x => x.MatchPlayers)
                .WithOne(x => x.Player)
                .HasForeignKey(x => x.PlayerId);

            builder.Property(x => x.Status)
                .HasConversion<short>()
                .HasDefaultValue(Status.Active)
                .IsRequired();

            builder.Property(x => x.InactivatedAt);

        });

        modelBuilder.Entity<UserEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.UserName).IsRequired();
            builder.Property(x => x.FirstName).IsRequired();
            builder.Property(x => x.LastName).IsRequired();
            builder.Property(x => x.Email).IsRequired();

            builder.HasMany(x => x.Players)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId);

            builder.Property(x => x.Status)
                .HasConversion<short>()
                .HasDefaultValue(Status.Active)
                .IsRequired();

            builder.Property(x => x.InactivatedAt);

        });

        modelBuilder.Entity<GroupEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name).IsRequired();

            builder.Property(x => x.CreatedByUserId).IsRequired();

            builder.HasMany(x => x.Players)
                .WithOne(x => x.Group)
                .HasForeignKey(x => x.GroupId);

            builder.Property(x => x.Status)
                .HasConversion<short>()
                .HasDefaultValue(Status.Active)
                .IsRequired();

            builder.Property(x => x.InactivatedAt);

        });

        modelBuilder.Entity<GroupAdminEntity>(builder =>
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
        });

        modelBuilder.Entity<TeamColorEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.GroupId)
                .IsRequired();

            builder.HasOne<GroupEntity>()
                .WithMany()
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(x => x.HexValue)
                .IsRequired()
                .HasMaxLength(10);

            builder.Property(x => x.IsActive)
                .HasDefaultValue(true)
                .IsRequired();

            builder.HasIndex(x => new { x.GroupId, x.Name }).IsUnique(false);
        });

        modelBuilder.Entity<GroupSettingsEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.GroupId)
                .IsRequired();

            builder.HasOne<GroupEntity>()
                .WithOne()  
                .HasForeignKey<GroupSettingsEntity>(x => x.GroupId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Property(x => x.MinPlayers).IsRequired();
            builder.Property(x => x.MaxPlayers).IsRequired();

            builder.Property(x => x.DefaultPlaceName)
                .HasMaxLength(200);

            builder.Property(x => x.DefaultDayOfWeek);
            builder.Property(x => x.DefaultKickoffTime);

            builder.HasIndex(x => x.GroupId).IsUnique();
        });

        modelBuilder.Entity<GoalEntity>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();

            builder.Property(x => x.MatchId).IsRequired();
            builder.Property(x => x.GroupId).IsRequired();

            builder.Property(x => x.ScorerMatchPlayerId).IsRequired();
            builder.Property(x => x.AssistMatchPlayerId).IsRequired(false);

            builder.Property(x => x.TimeSeconds).IsRequired(false);

            builder.Property(x => x.IsOwnGoal).IsRequired().HasDefaultValue(false);

            builder.HasOne(x => x.Match)
                .WithMany(m => m.Goals)
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Group)
                .WithMany()
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.ScorerMatchPlayer)
                .WithMany(mp => mp.GoalsScored)
                .HasForeignKey(x => x.ScorerMatchPlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.AssistMatchPlayer)
                .WithMany(mp => mp.GoalsAssisted)
                .HasForeignKey(x => x.AssistMatchPlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => new { x.MatchId, x.ScorerMatchPlayerId });
            builder.HasIndex(x => new { x.MatchId, x.AssistMatchPlayerId });
        });

        modelBuilder.Entity<GroupInviteEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Status)
                .HasConversion<short>()
                .HasDefaultValue(GroupInviteStatus.Pending)
                .IsRequired();

            builder.HasOne(x => x.Group)
                .WithMany()
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.TargetUser)
                .WithMany()
                .HasForeignKey(x => x.TargetUserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.GuestPlayer)
                .WithMany()
                .HasForeignKey(x => x.GuestPlayerId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            // índice para buscar convites pendentes de um usuário
            builder.HasIndex(x => new { x.TargetUserId, x.Status });
            // evitar convites duplicados pendentes para o mesmo par usuário+grupo
            builder.HasIndex(x => new { x.GroupId, x.TargetUserId, x.Status });
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
