using System;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

        modelBuilder.Entity<GroupFinanceiroEntity>(builder =>
        {
            builder.HasKey(x => new { x.UserId, x.GroupId });

            builder.HasOne(x => x.Group)
                .WithMany(x => x.Financeiros)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.User)
                .WithMany(x => x.Financeiros)
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

            builder.Property(x => x.PaymentMode)
                .HasConversion<short>()
                .HasDefaultValue(Domain.Enums.PaymentMode.Monthly);

            builder.Property(x => x.MvpTieRule)
                .HasConversion<short>()
                .HasDefaultValue(Domain.Enums.MvpTieRule.AllMvp);

            builder.Property(x => x.MvpTieMaxPlayers)
                .HasDefaultValue(2);

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

        modelBuilder.Entity<CalendarCategoryEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.GroupId).IsRequired();
            builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
            builder.Property(x => x.Color).HasMaxLength(20);
            builder.Property(x => x.Icon).HasMaxLength(100);
            builder.Property(x => x.IsSystem).IsRequired().HasDefaultValue(false);

            builder.HasOne<GroupEntity>()
                .WithMany()
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.GroupId);
        });

        modelBuilder.Entity<CalendarEventEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.GroupId).IsRequired();
            builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
            builder.Property(x => x.Description).HasMaxLength(1000);
            builder.Property(x => x.TimeTBD).IsRequired().HasDefaultValue(false);
            builder.Property(x => x.Icon).HasMaxLength(100);

            builder.HasOne<GroupEntity>()
                .WithMany()
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(x => new { x.GroupId, x.EventDate });
        });

        modelBuilder.Entity<PollEntity>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.GroupId).IsRequired();
            builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
            builder.Property(x => x.Description).HasMaxLength(1000);
            builder.Property(x => x.Status).IsRequired().HasMaxLength(20).HasDefaultValue("open");
            builder.Property(x => x.DeadlineDate).HasColumnType("date");
            builder.Property(x => x.DeadlineTime).HasColumnType("time without time zone");
            builder.Property(x => x.Type).IsRequired().HasMaxLength(20).HasDefaultValue("poll");
            builder.Property(x => x.EventDate).HasColumnType("date");
            builder.Property(x => x.EventTime).HasColumnType("time without time zone");
            builder.Property(x => x.EventLocation).HasMaxLength(300);
            builder.Property(x => x.EventIcon).HasMaxLength(100);
            builder.Property(x => x.CostType).HasMaxLength(20);
            builder.Property(x => x.CostAmount).HasPrecision(10, 2);
            builder.HasOne<GroupEntity>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            builder.HasMany(x => x.Options).WithOne(x => x.Poll).HasForeignKey(x => x.PollId).OnDelete(DeleteBehavior.Cascade);
            builder.HasMany(x => x.Votes).WithOne().HasForeignKey(x => x.PollId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => x.GroupId);
        });

        modelBuilder.Entity<PollOptionEntity>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.PollId).IsRequired();
            builder.Property(x => x.Text).IsRequired().HasMaxLength(300);
            builder.Property(x => x.Description).HasMaxLength(1000);
            builder.Property(x => x.ImageUrl).HasColumnType("text");
            builder.HasIndex(x => x.PollId);
        });

        modelBuilder.Entity<PollVoteEntity>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.HasIndex(x => new { x.PollId, x.PlayerId });
            builder.HasOne(x => x.Option).WithMany().HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MonthlyPaymentEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.GroupId).IsRequired();
            builder.Property(x => x.PlayerId).IsRequired();
            builder.Property(x => x.Year).IsRequired();
            builder.Property(x => x.Month).IsRequired();
            builder.Property(x => x.Amount).IsRequired().HasColumnType("numeric(10,2)");
            builder.Property(x => x.Discount).IsRequired().HasDefaultValue(0m).HasColumnType("numeric(10,2)");
            builder.Property(x => x.DiscountReason).HasMaxLength(500);
            builder.Property(x => x.Status)
                .HasConversion<short>()
                .HasDefaultValue(PaymentStatus.Pending)
                .IsRequired();

            builder.HasOne(x => x.Player)
                .WithMany()
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Group)
                .WithMany()
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            // Um registro por jogador por mês/ano
            builder.HasIndex(x => new { x.GroupId, x.PlayerId, x.Year, x.Month })
                .IsUnique();
        });

        modelBuilder.Entity<ExtraChargeEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.GroupId).IsRequired();
            builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
            builder.Property(x => x.Description).HasMaxLength(1000);
            builder.Property(x => x.Amount).IsRequired().HasColumnType("numeric(10,2)");
            builder.Property(x => x.IsCancelled).IsRequired().HasDefaultValue(false);

            builder.HasOne(x => x.Group)
                .WithMany()
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(x => x.Payments)
                .WithOne(x => x.ExtraCharge)
                .HasForeignKey(x => x.ExtraChargeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.GroupId);
        });

        modelBuilder.Entity<ExtraChargePaymentEntity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.ExtraChargeId).IsRequired();
            builder.Property(x => x.PlayerId).IsRequired();
            builder.Property(x => x.GroupId).IsRequired();
            builder.Property(x => x.Amount).IsRequired().HasColumnType("numeric(10,2)");
            builder.Property(x => x.Discount).IsRequired().HasDefaultValue(0m).HasColumnType("numeric(10,2)");
            builder.Property(x => x.DiscountReason).HasMaxLength(500);
            builder.Property(x => x.Status)
                .HasConversion<short>()
                .HasDefaultValue(PaymentStatus.Pending)
                .IsRequired();

            builder.Ignore(x => x.FinalAmount);

            builder.HasOne(x => x.ExtraCharge)
                .WithMany(x => x.Payments)
                .HasForeignKey(x => x.ExtraChargeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Player)
                .WithMany()
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Group)
                .WithMany()
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            // Um pagamento por jogador por cobrança
            builder.HasIndex(x => new { x.ExtraChargeId, x.PlayerId })
                .IsUnique();
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
