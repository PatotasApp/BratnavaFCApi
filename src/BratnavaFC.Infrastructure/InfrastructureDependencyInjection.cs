using BratnavaFC.Infrastructure.Cloudflare;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Redis;
using BratnavaFC.Infrastructure.Repositories;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Infrastructure;

public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger logger)
    {
        var isDevelopment = environment.IsDevelopment();

        services.AddDatabase(configuration);
        services.AddBackgroundJobs(configuration, isDevelopment);
        services.AddReplayEventing(configuration, environment, isDevelopment);
        services.AddReplayStorage(configuration, environment, isDevelopment);
        services.AddFirebase(configuration, logger);
        services.AddExternalHttpClients();

        logger.LogInformation(
            "[Startup] Infraestrutura registrada. Environment={Environment} Hangfire={Hangfire} Redis={Redis} CloudflareR2={R2}",
            environment.EnvironmentName,
            !isDevelopment,
            !isDevelopment,
            !isDevelopment);

        return services;
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped(typeof(IRepositoryBase<>), typeof(RepositoryBase<>));
    }

    private static void AddBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        if (isDevelopment)
            return;

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // Checagem de configuração, não de conectividade: o UsePostgreSqlStorage abre uma
        // conexão já no registro e lançaria "ConnectionString property has not been
        // initialized", derrubando o boot. Sem string configurada a API sobe sem Hangfire;
        // se a string existe mas o banco está fora, o Hangfire se recupera sozinho.
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                options => options.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions
                {
                    SchemaName = "hangfire",
                    QueuePollInterval = TimeSpan.FromHours(1)
                }));

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = 2;
            options.Queues = ["default"];
            options.SchedulePollingInterval = TimeSpan.FromMinutes(5);
        });
    }

    private static void AddReplayEventing(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        bool isDevelopment)
    {
        if (isDevelopment)
        {
            services.AddSingleton<IRedisConnectionProvider>(sp =>
                new NoOpRedisConnectionProvider(
                    sp.GetRequiredService<ILogger<NoOpRedisConnectionProvider>>(),
                    environment.EnvironmentName));

            services.AddScoped<IMatchEventPublisher>(sp =>
                new NoOpMatchEventPublisher(
                    sp.GetRequiredService<ILogger<NoOpMatchEventPublisher>>(),
                    environment.EnvironmentName));

            // ReplayStreamConsumerService fica fora: o loop XREADGROUP BLOCK manteria
            // a máquina ocupada indefinidamente sem ter stream nenhum para consumir.
            return;
        }

        services.AddSingleton<IRedisConnectionProvider>(sp =>
            new RedisConnectionProvider(
                configuration["ConnectionStrings:RedisConnection"]!,
                sp.GetRequiredService<ILogger<RedisConnectionProvider>>()));

        services.AddScoped<IMatchEventPublisher>(sp =>
            new RedisMatchEventPublisher(
                sp.GetRequiredService<IRedisConnectionProvider>(),
                sp.GetRequiredService<AppDbContext>(),
                sp.GetRequiredService<ILogger<RedisMatchEventPublisher>>(),
                "replay_events"));

        services.AddHostedService<ReplayStreamConsumerService>();
    }

    private static void AddReplayStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        bool isDevelopment)
    {
        if (isDevelopment)
        {
            // Sem o Bind/ValidateOnStart: as chaves do R2 não existem em Development, e a
            // validação derrubaria o startup local antes de a API subir.
            services.AddSingleton<IReplayUrlService>(sp =>
                new NoOpReplayUrlService(
                    sp.GetRequiredService<ILogger<NoOpReplayUrlService>>(),
                    environment.EnvironmentName));

            return;
        }

        services.AddOptions<R2Options>()
            .Bind(configuration.GetSection(R2Options.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.AccessKey)
                     && !string.IsNullOrWhiteSpace(o.SecretKey)
                     && !string.IsNullOrWhiteSpace(o.EndpointUrl),
                "Configuração do R2 incompleta.")
            .ValidateOnStart();

        services.AddSingleton<IReplayUrlService, R2ReplayUrlService>();
    }

    private static void AddFirebase(
        this IServiceCollection services,
        IConfiguration configuration,
        ILogger logger)
    {
        var firebaseJson = Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_JSON");
        try
        {
            if (!string.IsNullOrWhiteSpace(firebaseJson))
            {
                FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromJson(firebaseJson) });
                logger.LogInformation("[Firebase] Inicializado via ServiceAccountJson.");
            }
            else
            {
                FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.GetApplicationDefault() });
                logger.LogInformation("[Firebase] Inicializado via Application Default Credentials.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[Firebase] Não pôde ser inicializado. Push notifications estarão indisponíveis. " +
                "Verifique FIREBASE_SERVICE_ACCOUNT_JSON.");
        }
    }

    private static void AddExternalHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient("BrasilApi", c =>
        {
            c.BaseAddress = new Uri("https://brasilapi.com.br/");
            c.DefaultRequestHeaders.Add("Accept", "application/json");
            c.DefaultRequestHeaders.Add("User-Agent", "BratnavaFC/1.0");
            c.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient("OpenAI", c =>
        {
            c.BaseAddress = new Uri("https://api.openai.com/");
            c.DefaultRequestHeaders.Add("Accept", "application/json");
            c.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddMemoryCache(o => o.SizeLimit = 10_000);
    }
}
