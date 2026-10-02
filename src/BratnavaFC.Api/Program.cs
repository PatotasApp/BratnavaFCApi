using BratnavaFC.Api.Extensions;
using BratnavaFC.Application;
using BratnavaFC.Infrastructure;
using Serilog;

// Logger mínimo antes de a configuração ser lida, para que falha de startup
// (credencial ausente, banco inacessível) apareça em vez de morrer silenciosa.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // O perfil IIS Express pode executar com comportamento de Production para reproduzir
    // o Fly, mas ainda precisa das credenciais locais mantidas fora do repositório.
    // No servidor publicado esta fonte é vazia e os secrets continuam vindo do ambiente.
    builder.Configuration.AddUserSecrets<Program>(optional: true);

    builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    var startupLogger = LoggerFactory
        .Create(l => l.AddSerilog(Log.Logger))
        .CreateLogger("Startup");

    startupLogger.LogInformation(
        "[Startup] Iniciando. Environment={Environment}",
        builder.Environment.EnvironmentName);

    // Uma exceção no consumidor de replays não deve derrubar a API.
    builder.Services.Configure<HostOptions>(options =>
    {
        options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
    });

    builder.Services.AddInfrastructure(builder.Configuration, builder.Environment, startupLogger);
    builder.Services.AddApplication(builder.Configuration, builder.Environment);

    builder.Services.AddApiPresentation(builder.Environment, builder.Configuration);
    // Depois de AddInfrastructure: o ProjectId usado na validação do JWT sai do service
    // account JSON que ela carrega, e de mais lugar nenhum.
    builder.Services.AddJwtAuthentication();
    builder.Services.AddRealtime(builder.Environment);

    // Health checks estão implementados em Api/HealthChecks e Api/Extensions/HealthCheckExtensions,
    // mas propositalmente NÃO injetados. Motivo: o poller do dashboard é um IHostedService que
    // sobe com o processo e consulta o banco a cada 60s, mesmo sem ninguém abrir a UI — e o Neon
    // só suspende o compute após 5 minutos de inatividade, então o banco nunca dormiria.
    // Para reativar, basta descomentar esta linha e o MapHealthProbes() abaixo.
    // builder.Services.AddApplicationHealthChecks(builder.Environment);

    var app = builder.Build();

    app.UseApiPipeline();
    app.UseBackgroundJobs();

    // Ver a nota em AddApplicationHealthChecks acima.
    // app.MapHealthProbes();

    app.MapControllers();
    app.MapRealtimeHub();

    startupLogger.LogInformation("[Startup] Endpoints mapeados. Iniciando app.Run().");
    app.Run();
}
catch (HostAbortedException)
{
    // O tooling do Entity Framework aborta o host propositalmente depois de
    // resolver o DbContext. Isso não representa falha da API nem da migration.
    Log.Information("[Startup] Host encerrado pelo tooling do Entity Framework.");
}
catch (Exception ex)
{
    Log.Fatal(ex, "[Startup] Host encerrou de forma inesperada.");
    throw; // Mantém o exit code diferente de zero — o Fly precisa saber que o deploy falhou.
}
finally
{
    Log.CloseAndFlush();
}
