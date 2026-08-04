using BratnavaFC.Infrastructure.Cloudflare;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Redis;
using BratnavaFC.Infrastructure.Repositories;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Hangfire;
using Hangfire.Redis.StackExchange;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

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
        services.AddBackgroundJobs(configuration, environment, logger);
        services.AddReplayEventing(configuration, environment, isDevelopment);
        services.AddReplayStorage(configuration, environment, isDevelopment);
        services.AddFirebase(configuration, logger);
        services.AddExternalHttpClients();

        logger.LogInformation(
            "[Startup] Infraestrutura registrada. Environment={Environment} Hangfire={Hangfire} Redis={Redis} CloudflareR2={R2}",
            environment.EnvironmentName,
            BackgroundJobsGate.IsEnabled(configuration, environment),
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
        IHostEnvironment environment,
        ILogger logger)
    {
        if (!BackgroundJobsGate.IsEnabled(configuration, environment))
        {
            // Fora do Development, o gate desligado normalmente significa um secret
            // esquecido (RedisConnection é um Fly secret em produção) — sem este aviso a
            // única pista seria o "Hangfire=False" no log de Information, fácil de passar
            // batido. Em Development o Hangfire ficar desligado é o esperado, não um erro.
            if (!environment.IsDevelopment())
            {
                logger.LogWarning(
                    "[Startup] Hangfire desligado: ConnectionStrings:RedisConnection não configurada.");
            }

            return;
        }

        var connectionString = configuration.GetConnectionString("RedisConnection")!;

        // Multiplexer dedicado, separado do IRedisConnectionProvider que serve os replays:
        // o ReplayStreamConsumerService estaciona um XREADGROUP BLOCK de 30s na conexão
        // dele, e o StackExchange.Redis multiplexa — comandos do Hangfire ficariam na fila
        // atrás do bloqueio.
        var options = ConfigurationOptions.Parse(connectionString);

        // O storage do Redis abre a conexão já aqui no registro (Connect, mais abaixo). Com
        // AbortOnConnectFail=false o Connect não lança, então Redis fora do ar no boot não
        // derruba o deploy — o multiplexer fica tentando reconectar sozinho em background.
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 10_000;
        options.SyncTimeout = 40_000;

        // Sem isso o StackExchange.Redis manda PING de keep-alive a cada 60s por padrão em
        // cada bridge de conexão — e este multiplexer é o segundo apontando para a mesma
        // instância Upstash, então esses PINGs não entravam na conta do orçamento de 500k
        // comandos/mês. 180s ainda detecta conexão morta bem dentro dos 20 min do
        // ServerTimeout do Hangfire Server, só com uma folga bem maior entre pings.
        options.KeepAlive = 180;

        var multiplexer = ConnectionMultiplexer.Connect(options);

        // Registrado como instância (não como factory) para ter um dono único e escopo de
        // vida igual ao do processo. O AddSingleton(instance) é um call site constante — o
        // container não o rastreia como IDisposable e não vai chamá-lo no shutdown. Isso é
        // aceitável aqui: a conexão precisa sobreviver ao próprio shutdown do Hangfire, que
        // ainda usa este multiplexer para persistir o estado dos jobs em andamento antes de
        // o processo encerrar.
        services.AddSingleton(multiplexer);

        // AutomaticRetryAttribute já vem em GlobalJobFilters.Filters e tem
        // AllowMultiple = false, então adicionar um segundo via UseFilter deixaria a
        // precedência entre os dois indefinida. Ajustamos a instância registrada.
        // 10 tentativas (padrão) gravam 10 stack traces no history de cada job que falha
        // até o fim, e é isso que transforma rajada de falha em dezenas de MB no Redis.
        GlobalJobFilters.Filters
            .Select(f => f.Instance)
            .OfType<AutomaticRetryAttribute>()
            .Single()
            .Attempts = 3;

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseRedisStorage(multiplexer, new RedisStorageOptions
            {
                // As chaves {} são hash tag de cluster e mantêm todas as chaves do Hangfire
                // no mesmo slot — o padrão do pacote é "{hangfire}:" pelo mesmo motivo.
                // Sem elas, operação multi-key quebraria com CROSSSLOT num Redis clusterizado.
                Prefix = "{app-hangfire}:",

                // Free tier do Upstash só tem o db 0.
                Db = 0,

                // Os intervalos abaixo existem para caber nos 500k comandos/mês do plano
                // gratuito. Nos padrões (fetch 3min, listas 499) o consumo seria bem maior.
                FetchTimeout = TimeSpan.FromMinutes(5),
                InvisibilityTimeout = TimeSpan.FromMinutes(30),
                ExpiryCheckInterval = TimeSpan.FromHours(1),
                SucceededListSize = 50,
                DeletedListSize = 50,
            })
            // Encadeado depois do UseRedisStorage porque a extensão é sobre
            // IGlobalConfiguration<TStorage> — só existe com o storage já tipado.
            // Vale para Succeeded e Deleted, os dois estados finais do Hangfire.
            .WithJobExpirationTimeout(TimeSpan.FromHours(12)));

        services.AddHangfireServer(options =>
        {
            // 1 worker basta: são 3 jobs recorrentes e lembretes esparsos. O padrão é 20,
            // e cada worker custa um fetch bloqueante recorrente no orçamento do Upstash.
            options.WorkerCount = 1;
            options.Queues = ["default"];

            // Padrões seriam 15s de polling e 30s de heartbeat — só o heartbeat daria
            // ~172k comandos/mês. Precisão de 5 min é irrelevante para lembretes que são
            // agendados 24h e 2h antes do evento.
            options.SchedulePollingInterval = TimeSpan.FromMinutes(5);
            options.HeartbeatInterval = TimeSpan.FromMinutes(5);

            // ServerTimeout tem que ser bem maior que o HeartbeatInterval. O custo é que o
            // registro de uma máquina morta sobrevive até 20 min; produção roda com
            // auto_stop_machines = false, então a máquina é estável.
            options.ServerTimeout = TimeSpan.FromMinutes(20);
            options.ServerCheckInterval = TimeSpan.FromMinutes(15);
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
