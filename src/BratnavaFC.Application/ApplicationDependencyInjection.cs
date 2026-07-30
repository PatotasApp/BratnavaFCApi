using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Application.Validators;
using BratnavaFC.Domain.Entities;
using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application;

public static class ApplicationDependencyInjection
{
    /// <summary>
    /// Registra os serviços de negócio. Em Development os handlers de job recorrente ficam
    /// fora — só o Hangfire os resolve, e ele não sobe nesse ambiente — e o agendamento cai
    /// no <see cref="NoOpNotificationScheduler"/>.
    /// </summary>
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        var isDevelopment = environment.IsDevelopment();

        services.AddValidators();
        services.AddDomainServices();
        services.AddNotificationScheduling(isDevelopment);
        services.AddRecurringJobHandlers(isDevelopment);
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

        // AddOrUpdate persiste o job no storage, então tirar o código não basta: sem o
        // RemoveIfExists estes dois continuariam disparando a partir do banco.
        recurringJobs.RemoveIfExists("match-scheduler");
        recurringJobs.RemoveIfExists("birthday-daily");

        logger.LogInformation("[Startup] 2 jobs recorrentes registrados, 2 removidos do storage.");
    }

    /// <summary>
    /// Registra todo IValidator&lt;T&gt; deste assembly. Os services os injetam e chamam
    /// explicitamente, em vez de validação automática no pipeline do MVC, para que o erro
    /// saia como Result.Fail — no mesmo envelope que o resto da API devolve.
    /// </summary>
    private static void AddValidators(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateUserDtoValidator>(ServiceLifetime.Singleton);
    }

    private static void AddDomainServices(this IServiceCollection services)
    {
        services.AddScoped<IMatchService, MatchService>();
        services.AddScoped<IPlayerStatsService, PlayerStatsService>();
        services.AddScoped<ITeamColorService, TeamColorService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<TeamGenerationService>();
        services.AddScoped<PasswordHasher<UserEntity>>();
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

    private static void AddNotificationScheduling(this IServiceCollection services, bool isDevelopment)
    {
        if (isDevelopment)
            services.AddScoped<INotificationScheduler, NoOpNotificationScheduler>();
        else
            services.AddScoped<INotificationScheduler, NotificationScheduler>();
    }

    private static void AddRecurringJobHandlers(this IServiceCollection services, bool isDevelopment)
    {
        if (isDevelopment)
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
    }

    private static void AddHolidayService(this IServiceCollection services)
    {
        // Consome o named client "BrasilApi" e o IMemoryCache registrados na infraestrutura.
        services.AddSingleton<IHolidayService, HolidayService>();
    }
}
