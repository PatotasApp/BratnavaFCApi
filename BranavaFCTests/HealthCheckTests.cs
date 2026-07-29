using BratnavaFC.Api.Extensions;
using BratnavaFC.Api.HealthChecks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Moq;

namespace BranavaFC.Tests;

public class HealthCheckTests
{
    // ── Gate de ambiente ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("hangfire")]
    [InlineData("redis")]
    [InlineData("cloudflare-r2")]
    public void Development_registra_DisabledDependencyHealthCheck_para_dependencias_desligadas(string name)
    {
        var registration = ResolveRegistration("Development", name);
        var check = registration.Factory(new ServiceCollection().BuildServiceProvider());

        check.Should().BeOfType<DisabledDependencyHealthCheck>();
        registration.FailureStatus.Should().Be(HealthStatus.Healthy);
    }

    [Theory]
    [InlineData("hangfire", typeof(HangfireHealthCheck))]
    [InlineData("redis", typeof(RedisHealthCheck))]
    [InlineData("cloudflare-r2", typeof(CloudflareR2HealthCheck))]
    public void Production_registra_o_check_real_com_FailureStatus_Degraded(string name, Type expected)
    {
        var registration = ResolveRegistration("Production", name);
        var check = registration.Factory(BuildDependencyProvider());

        check.Should().BeOfType(expected);
        registration.FailureStatus.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public void Development_e_Production_expoem_o_mesmo_conjunto_de_checks()
    {
        var development = ResolveRegistrations("Development").Select(r => r.Name);
        var production = ResolveRegistrations("Production").Select(r => r.Name);

        development.Should().BeEquivalentTo(production);
        development.Should().BeEquivalentTo(
            "database", "hangfire", "redis", "firebase", "cloudflare-r2", "openai", "brasil-api");
    }

    [Fact]
    public async Task Dependencia_desabilitada_nao_degrada_o_status_geral()
    {
        var report = await RunAsync("Development", registration => registration.Name == "redis");

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries["redis"].Status.Should().Be(HealthStatus.Healthy);
        report.Entries["redis"].Description.Should().Be("Desabilitado no ambiente Development.");
    }

    // ── Readiness ────────────────────────────────────────────────────────────

    [Fact]
    public void Apenas_o_banco_entra_na_readiness()
    {
        var ready = ResolveRegistrations("Production")
            .Where(r => r.Tags.Contains("ready"))
            .Select(r => r.Name);

        ready.Should().Equal("database");
    }

    [Fact]
    public void Banco_derruba_o_status_geral_e_as_demais_dependencias_apenas_degradam()
    {
        var registrations = ResolveRegistrations("Production").ToDictionary(r => r.Name);

        registrations["database"].FailureStatus.Should().Be(HealthStatus.Unhealthy);

        registrations
            .Where(r => r.Key != "database")
            .Should().OnlyContain(r => r.Value.FailureStatus == HealthStatus.Degraded);
    }

    // ── Timeout e exceções ───────────────────────────────────────────────────

    [Fact]
    public async Task Timeout_do_registro_respeita_o_FailureStatus_configurado()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().Add(new HealthCheckRegistration(
            "slow",
            _ => new SlowHealthCheck(),
            HealthStatus.Degraded,
            tags: null,
            timeout: TimeSpan.FromMilliseconds(50)));

        var report = await services.BuildServiceProvider()
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync();

        // Sem o catch de OperationCanceledException no HealthCheckRunner, o framework
        // reportaria Unhealthy e uma lentidão do Redis derrubaria o status geral.
        report.Entries["slow"].Status.Should().Be(HealthStatus.Degraded);
        report.Entries["slow"].Description.Should().Be("Timeout ao consultar a dependencia.");
    }

    [Fact]
    public async Task Excecao_no_corpo_do_check_respeita_o_FailureStatus_configurado()
    {
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(
                "boom",
                _ => new SlowHealthCheck(),
                HealthStatus.Degraded,
                tags: null),
        };

        var result = await HealthCheckRunner.RunAsync(
            context,
            _ => throw new InvalidOperationException("falhou"),
            CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("InvalidOperationException: falhou");
    }

    // ── Check de dependência desabilitada ────────────────────────────────────

    [Fact]
    public async Task DisabledDependencyHealthCheck_reporta_Healthy_com_a_razao()
    {
        var check = new DisabledDependencyHealthCheck("Development");

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Desabilitado no ambiente Development.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<HealthReport> RunAsync(
        string environmentName,
        Func<HealthCheckRegistration, bool> predicate)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationHealthChecks(new FakeEnvironment(environmentName));

        return await services.BuildServiceProvider()
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(predicate, CancellationToken.None);
    }

    private static HealthCheckRegistration ResolveRegistration(string environmentName, string name)
        => ResolveRegistrations(environmentName).Single(r => r.Name == name);

    private static IReadOnlyList<HealthCheckRegistration> ResolveRegistrations(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddApplicationHealthChecks(new FakeEnvironment(environmentName));

        return services.BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations
            .ToList();
    }

    private static IServiceProvider BuildDependencyProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(Mock.Of<IRedisConnectionProvider>());
        services.AddSingleton(Mock.Of<IHttpClientFactory>());
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        return services.BuildServiceProvider();
    }

    private sealed class SlowHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken ct = default)
            => HealthCheckRunner.RunAsync(context, async token =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                return HealthCheckResult.Healthy();
            }, ct);
    }

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
