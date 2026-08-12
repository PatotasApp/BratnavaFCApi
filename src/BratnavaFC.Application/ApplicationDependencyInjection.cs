using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application;

public static class ApplicationDependencyInjection
{
    /// <summary>
    /// Registra os serviços de negócio. Os handlers de job recorrente e o agendamento real
    /// só entram quando os jobs em background estão ligados — ver
    /// <see cref="BackgroundJobsGate"/>. Sem isso, o agendamento cai no
    /// <see cref="NoOpNotificationScheduler"/>, porque o real depende de
    /// <c>IBackgroundJobClient</c>, que só existe junto com o Hangfire.
    /// </summary>
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var backgroundJobsEnabled = BackgroundJobsGate.IsEnabled(configuration, environment);

        services.AddDomainServices();
        services.AddNotificationScheduling(backgroundJobsEnabled);
        services.AddRecurringJobHandlers(backgroundJobsEnabled);
        services.AddHolidayService();

        return services;
    }

    /// <summary>
    /// Define o cronograma dos jobs recorrentes. É regra de negócio, não infraestrutura,
    /// por isso vive aqui e não no registro do Hangfire. Chamado após o build, porque
    /// <see cref="IRecurringJobManager"/> só existe no container pronto.
    /// </summary>
    public static void UseRecurringJobs(IRecurringJobManager recurringJobs, ILogger logger)
    {
        var utc = new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc };

        recurringJobs.AddOrUpdate<IClipCleanupJob>(
            "clip-r2-cleanup",
            job => job.ExecuteAsync(CancellationToken.None),
            "0 3 1,15 * *",
            utc);

        recurringJobs.AddOrUpdate<IMonthlyPaymentReminderJob>(
            "monthly-payment-reminder",
            job => job.ExecuteAsync(CancellationToken.None),
            "0 13 10,20 * *",
            utc);

        recurringJobs.AddOrUpdate<IFailedJobCleanupJob>(
            "failed-job-cleanup",
            job => job.ExecuteAsync(CancellationToken.None),
            "0 4 10,20 * *",
            utc);

        // AddOrUpdate persiste o job no storage, então tirar o código não basta: sem o
        // RemoveIfExists estes dois continuariam disparando a partir do banco.
        recurringJobs.RemoveIfExists("match-scheduler");
        recurringJobs.RemoveIfExists("birthday-daily");

        logger.LogInformation("[Startup] 3 jobs recorrentes registrados, 2 removidos do storage.");
    }

    private static void AddDomainServices(this IServiceCollection services)
    {
        services.AddScoped<IMatchService, MatchService>();
        services.AddScoped<IPlayerStatsService, PlayerStatsService>();
        services.AddScoped<IConquistaService, ConquistaService>();
        services.AddScoped<IConquistaProjectionService, ConquistaProjectionService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<ITeamColorService, TeamColorService>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<TeamGenerationService>();
        services.AddScoped<IGroupSettingsService, GroupSettingsService>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<IFinancialTransactionService, FinancialTransactionService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPollService, PollService>();
        services.AddScoped<IPushService, PushService>();
        services.AddScoped<IAbsenceService, AbsenceService>();
        services.AddScoped<IBetService, BetService>();
        services.AddScoped<IMatchCardService, MatchCardService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ITeamBuilderService, TeamBuilderService>();
    }

    private static void AddNotificationScheduling(
        this IServiceCollection services, bool backgroundJobsEnabled)
    {
        if (backgroundJobsEnabled)
            services.AddScoped<INotificationScheduler, NotificationScheduler>();
        else
            services.AddScoped<INotificationScheduler, NoOpNotificationScheduler>();
    }

    private static void AddRecurringJobHandlers(
        this IServiceCollection services, bool backgroundJobsEnabled)
    {
        if (!backgroundJobsEnabled)
            return;

        services.AddScoped<IClipCleanupJob, ClipCleanupJob>();
        services.AddScoped<IMatchReminderJob, MatchReminderJob>();
        services.AddScoped<IPollReminderJob, PollReminderJob>();
        services.AddScoped<ICalendarReminderJob, CalendarReminderJob>();
        services.AddScoped<IBirthdayNotificationJob, BirthdayNotificationJob>();
        services.AddScoped<IMatchNoQuorumReminderJob, MatchNoQuorumReminderJob>();
        services.AddScoped<IMvpVotingReminderJob, MvpVotingReminderJob>();
        services.AddScoped<IMatchAutoFinalizeJob, MatchAutoFinalizeJob>();
        services.AddScoped<IMonthlyPaymentReminderJob, MonthlyPaymentReminderJob>();
        services.AddScoped<IMatchSchedulerJob, MatchSchedulerJob>();
        services.AddScoped<IFailedJobCleanupJob, FailedJobCleanupJob>();
    }

    private static void AddHolidayService(this IServiceCollection services)
    {
        // Consome o named client "BrasilApi" e o IMemoryCache registrados na infraestrutura.
        services.AddSingleton<IHolidayService, HolidayService>();
    }
}
