using BratnavaFC.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BranavaFC.Tests;

/// <summary>
/// O gate decide se Hangfire, IBackgroundJobClient e os handlers de job recorrente são
/// registrados. Os três wirings precisam concordar: divergirem significa que o
/// NotificationScheduler não resolve IBackgroundJobClient, ou que o pipeline pede
/// IRecurringJobManager a um container que não o tem.
/// </summary>
public class BackgroundJobsGateTests
{
    [Theory]
    // Development nunca liga, mesmo com Redis configurado.
    [InlineData("Development", "localhost:6379", false)]
    // Fora de Development, o que decide é ter RedisConnection. É assim que o app de dev no
    // Fly — que roda como Production mas não tem Redis provisionado — se autodesliga.
    [InlineData("Production", "localhost:6379", true)]
    [InlineData("Staging", "localhost:6379", true)]
    [InlineData("Production", "", false)]
    [InlineData("Production", null, false)]
    [InlineData("Production", "   ", false)]
    public void Gate_requires_non_development_environment_and_a_RedisConnection(
        string environmentName, string? redisConnection, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RedisConnection"] = redisConnection,
            })
            .Build();

        BackgroundJobsGate
            .IsEnabled(configuration, new FakeHostEnvironment(environmentName))
            .Should().Be(expected);
    }

    [Fact]
    public void Gate_is_off_when_the_connection_string_key_is_absent()
    {
        // Configuração totalmente vazia — nem a seção ConnectionStrings existe.
        var configuration = new ConfigurationBuilder().Build();

        BackgroundJobsGate
            .IsEnabled(configuration, new FakeHostEnvironment("Production"))
            .Should().BeFalse();
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "BratnavaFC.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
