using BratnavaFC.Api.Extensions;
using BratnavaFC.Api.Realtime;
using BratnavaFC.Application;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace BranavaFC.Tests;

/// <summary>
/// Em Development nada que manteria a máquina de pé pode ser registrado: hosted services,
/// worker do Hangfire e o hub SignalR ficam fora, e as dependências viram no-op.
/// </summary>
public class EnvironmentGateTests
{
    // ── Nada em background em Development ────────────────────────────────────

    [Fact]
    public void Development_nao_registra_hosted_service_nenhum()
    {
        var services = BuildInfrastructure("Development");

        // O ReplayStreamConsumerService faria XREADGROUP BLOCK em loop eterno.
        services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void Production_registra_o_consumidor_de_replays_e_o_servidor_Hangfire()
    {
        var services = BuildInfrastructure("Production");

        services.Should().Contain(d => d.ServiceType == typeof(IHostedService));
    }

    // ── Redis ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Development", typeof(NoOpRedisConnectionProvider))]
    [InlineData("Production", typeof(RedisConnectionProvider))]
    public void Provider_de_Redis_muda_com_o_ambiente(string environmentName, Type expected)
    {
        Resolve<IRedisConnectionProvider>(environmentName).Should().BeOfType(expected);
    }

    [Theory]
    [InlineData("Development", typeof(NoOpMatchEventPublisher))]
    [InlineData("Production", typeof(RedisMatchEventPublisher))]
    public void Publisher_de_eventos_muda_com_o_ambiente(string environmentName, Type expected)
    {
        Resolve<IMatchEventPublisher>(environmentName).Should().BeOfType(expected);
    }

    // ── Cloudflare R2 ────────────────────────────────────────────────────────

    [Fact]
    public void Development_usa_o_storage_no_op_sem_validar_credenciais()
    {
        // Em Production o ValidateOnStart exigiria as chaves do R2, que não existem local.
        Resolve<IReplayUrlService>("Development").Should().BeOfType<NoOpReplayUrlService>();
    }

    // ── Agendamento e handlers de job ────────────────────────────────────────

    [Theory]
    [InlineData("Development", typeof(NoOpNotificationScheduler))]
    [InlineData("Production", typeof(NotificationScheduler))]
    public void Scheduler_de_notificacao_muda_com_o_ambiente(string environmentName, Type expected)
    {
        var descriptor = BuildApplication(environmentName)
            .Single(d => d.ServiceType == typeof(INotificationScheduler));

        descriptor.ImplementationType.Should().Be(expected);
    }

    [Fact]
    public void Development_nao_registra_handlers_de_job_recorrente()
    {
        // Só o Hangfire os resolve, e ele não sobe nesse ambiente.
        BuildApplication("Development")
            .Should().NotContain(d => d.ServiceType == typeof(IClipCleanupJob));
    }

    [Fact]
    public void Production_registra_handlers_de_job_recorrente()
    {
        BuildApplication("Production")
            .Should().Contain(d => d.ServiceType == typeof(IClipCleanupJob));
    }

    // ── SignalR ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Development", typeof(NoOpRealtimeNotifier))]
    [InlineData("Production", typeof(SignalRRealtimeNotifier))]
    public void Notifier_de_realtime_muda_com_o_ambiente(string environmentName, Type expected)
    {
        var descriptor = BuildRealtime(environmentName)
            .Single(d => d.ServiceType == typeof(IRealtimeNotifier));

        descriptor.ImplementationType.Should().Be(expected);
    }

    [Fact]
    public void Development_nao_registra_SignalR()
    {
        // O hub é a única dependência que de fato impede o auto-stop da máquina no Fly.
        BuildRealtime("Development")
            .Should().NotContain(d => d.ServiceType.FullName!.Contains("SignalR"));
    }

    [Fact]
    public void Production_registra_SignalR()
    {
        BuildRealtime("Production")
            .Should().Contain(d => d.ServiceType.FullName!.Contains("SignalR"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static T Resolve<T>(string environmentName) where T : notnull
    {
        var services = BuildInfrastructure(environmentName);
        services.AddLogging();

        return services.BuildServiceProvider().GetRequiredService<T>();
    }

    private static IServiceCollection BuildInfrastructure(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddInfrastructure(
            BuildConfiguration(),
            new FakeEnvironment(environmentName),
            NullLogger.Instance);

        return services;
    }

    private static IServiceCollection BuildApplication(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddApplication(new FakeEnvironment(environmentName));
        return services;
    }

    private static IServiceCollection BuildRealtime(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddRealtime(new FakeEnvironment(environmentName));
        return services;
    }

    private static IConfiguration BuildConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=u;Password=p;",
                ["Redis:ConnectionString"] = "localhost:6379",
                ["Cloudflare:R2:AccessKey"] = "key",
                ["Cloudflare:R2:SecretKey"] = "secret",
                ["Cloudflare:R2:EndpointUrl"] = "https://r2.example.com",
            })
            .Build();

    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public FakeEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "BratnavaFC.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
