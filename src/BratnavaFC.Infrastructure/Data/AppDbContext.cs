using System;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data.Configurations;
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
    public DbSet<GroupFinanceiroEntity> GroupFinanceiros => Set<GroupFinanceiroEntity>();
    public DbSet<PlayerEntity> Players => Set<PlayerEntity>();
    public DbSet<GroupSettingsEntity> GroupSettings => Set<GroupSettingsEntity>();
    public DbSet<GoalEntity> Goals => Set<GoalEntity>();
    public DbSet<GroupInviteEntity> GroupInvites => Set<GroupInviteEntity>();
    public DbSet<CalendarCategoryEntity> CalendarCategories => Set<CalendarCategoryEntity>();
    public DbSet<CalendarEventEntity> CalendarEvents => Set<CalendarEventEntity>();
    public DbSet<MonthlyPaymentEntity> MonthlyPayments => Set<MonthlyPaymentEntity>();
    public DbSet<ExtraChargeEntity> ExtraCharges => Set<ExtraChargeEntity>();
    public DbSet<ExtraChargePaymentEntity> ExtraChargePayments => Set<ExtraChargePaymentEntity>();
    public DbSet<PollEntity> Polls => Set<PollEntity>();
    public DbSet<PollOptionEntity> PollOptions => Set<PollOptionEntity>();
    public DbSet<PollVoteEntity> PollVotes => Set<PollVoteEntity>();
    public DbSet<PollOptionImageEntity> PollOptionImages => Set<PollOptionImageEntity>();
    public DbSet<PollGuestEntity> PollGuests => Set<PollGuestEntity>();
    public DbSet<PushTokenEntity> PushTokens => Set<PushTokenEntity>();
    public DbSet<UserAbsenceEntity> UserAbsences => Set<UserAbsenceEntity>();
    public DbSet<ReplayClipEntity> ReplayClips => Set<ReplayClipEntity>();
    public DbSet<ReplayLikeEntity> ReplayLikes => Set<ReplayLikeEntity>();
    public DbSet<ReplayFavoriteEntity> ReplayFavorites => Set<ReplayFavoriteEntity>();
    public DbSet<ReplayEventOutboxEntity> ReplayEventOutbox => Set<ReplayEventOutboxEntity>();
    public DbSet<MatchBetEntity> MatchBets => Set<MatchBetEntity>();
    public DbSet<MatchBetSelectionEntity> MatchBetSelections => Set<MatchBetSelectionEntity>();
    public DbSet<UserBetBalanceEntity> UserBetBalances => Set<UserBetBalanceEntity>();
    public DbSet<ScheduledNotificationJobEntity> ScheduledNotificationJobs => Set<ScheduledNotificationJobEntity>();
    public DbSet<UserNotificationEntity> UserNotifications => Set<UserNotificationEntity>();
    public DbSet<GroupTransactionEntity> GroupTransactions => Set<GroupTransactionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── Notifications ────────────────────────────────────────────────────
        modelBuilder.ApplyConfiguration(new UserNotificationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ScheduledNotificationJobEntityConfiguration());

        // ── Replay ───────────────────────────────────────────────────────────
        modelBuilder.ApplyConfiguration(new ReplayEventOutboxEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ReplayClipEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ReplayLikeEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ReplayFavoriteEntityConfiguration());

        // ── Match ────────────────────────────────────────────────────────────
        modelBuilder.ApplyConfiguration(new MatchEntityConfiguration());
        modelBuilder.ApplyConfiguration(new MatchPlayerEntityConfiguration());
        modelBuilder.ApplyConfiguration(new VoteEntityConfiguration());
        modelBuilder.ApplyConfiguration(new GoalEntityConfiguration());
        modelBuilder.ApplyConfiguration(new TeamColorEntityConfiguration());

        // ── Users / Groups ───────────────────────────────────────────────────
        modelBuilder.ApplyConfiguration(new PlayerEntityConfiguration());
        modelBuilder.ApplyConfiguration(new UserEntityConfiguration());
        modelBuilder.ApplyConfiguration(new GroupEntityConfiguration());
        modelBuilder.ApplyConfiguration(new GroupAdminEntityConfiguration());
        modelBuilder.ApplyConfiguration(new GroupFinanceiroEntityConfiguration());
        modelBuilder.ApplyConfiguration(new GroupSettingsEntityConfiguration());
        modelBuilder.ApplyConfiguration(new GroupInviteEntityConfiguration());
        modelBuilder.ApplyConfiguration(new UserAbsenceEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PushTokenEntityConfiguration());

        // ── Calendar ─────────────────────────────────────────────────────────
        modelBuilder.ApplyConfiguration(new CalendarCategoryEntityConfiguration());
        modelBuilder.ApplyConfiguration(new CalendarEventEntityConfiguration());

        // ── Polls ────────────────────────────────────────────────────────────
        modelBuilder.ApplyConfiguration(new PollEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PollOptionEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PollOptionImageEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PollVoteEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PollGuestEntityConfiguration());

        // ── Payments ─────────────────────────────────────────────────────────
        modelBuilder.ApplyConfiguration(new MonthlyPaymentEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ExtraChargeEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ExtraChargePaymentEntityConfiguration());

        // ── Bet ──────────────────────────────────────────────────────────────
        modelBuilder.ApplyConfiguration(new MatchBetEntityConfiguration());
        modelBuilder.ApplyConfiguration(new MatchBetSelectionEntityConfiguration());
        modelBuilder.ApplyConfiguration(new UserBetBalanceEntityConfiguration());

        // ── Financial Transactions ───────────────────────────────────────────
        modelBuilder.ApplyConfiguration(new GroupTransactionEntityConfiguration());
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
                // O construtor de BaseEntity já inicializa CreateDate; só preenche se estiver zerado,
                // preservando valores definidos explicitamente (ex.: seeds e testes).
                var createDate = entry.Property(nameof(BaseEntity.CreateDate));
                if (createDate.CurrentValue is not DateTime dt || dt == default)
                    createDate.CurrentValue = utcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(BaseEntity.CreateDate)).IsModified = false;
                entry.Property(nameof(BaseEntity.UpdateDate)).CurrentValue = utcNow;
            }
        }
    }
}
