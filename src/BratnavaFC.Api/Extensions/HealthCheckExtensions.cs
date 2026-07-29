using BratnavaFC.Api.HealthChecks;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BratnavaFC.Api.Extensions;

public static class HealthCheckExtensions
{
    /// <summary>Tag que define o conjunto de dependências consultadas por /readyz.</summary>
    private const string ReadyTag = "ready";

    private const string HealthzPath = "/healthz";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddApplicationHealthChecks(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        var checks = services.AddHealthChecks();

        // Único check da readiness: sem banco a API não serve nada. Todas as demais
        // dependências são degradáveis — a API segue online sem elas, então tirá-la do
        // balanceador por causa de uma queda do Upstash seria indisponibilidade autoinfligida.
        checks.AddDependency<DatabaseHealthCheck>(
            DatabaseHealthCheck.Name,
            HealthStatus.Unhealthy,
            [ReadyTag]);

        checks.AddEnvironmentGated<HangfireHealthCheck>(HangfireHealthCheck.Name, environment);
        checks.AddEnvironmentGated<RedisHealthCheck>(RedisHealthCheck.Name, environment);

        checks.AddDependency<FirebaseHealthCheck>(FirebaseHealthCheck.Name, HealthStatus.Degraded, []);

        checks.AddEnvironmentGated<CloudflareR2HealthCheck>(CloudflareR2HealthCheck.Name, environment);

        checks.AddDependency<OpenAiHealthCheck>(OpenAiHealthCheck.Name, HealthStatus.Degraded, []);
        checks.AddDependency<BrasilApiHealthCheck>(BrasilApiHealthCheck.Name, HealthStatus.Degraded, []);

        services
            .AddHealthChecksUI(settings =>
            {
                settings.AddHealthCheckEndpoint("BratnavaFC API", HealthzPath);
                settings.SetEvaluationTimeInSeconds(60);
                settings.SetMinimumSecondsBetweenFailureNotifications(120);
                settings.MaximumHistoryEntriesPerEndpoint(50);
            })
            .AddInMemoryStorage();

        return services;
    }

    public static WebApplication MapHealthProbes(this WebApplication app)
    {
        var liveness = new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = LivenessResponseWriter.WriteAsync,
        };

        app.MapHealthChecks("/livez", liveness).AllowAnonymous();
        app.MapHealthChecks("/health", liveness).AllowAnonymous();

        app.MapHealthChecks("/readyz", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        }).AllowAnonymous();

        // Formato que o UI consome; também serve para diagnóstico manual.
        app.MapHealthChecks(HealthzPath, new HealthCheckOptions
        {
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        }).AllowAnonymous();

        app.MapHealthChecksUI(options =>
        {
            options.UIPath = "/healthchecks-ui";
            options.ApiPath = "/healthchecks-api";
        }).AllowAnonymous();

        return app;
    }

    private static IHealthChecksBuilder AddEnvironmentGated<TCheck>(
        this IHealthChecksBuilder builder,
        string name,
        IWebHostEnvironment environment)
        where TCheck : class, IHealthCheck
        => environment.IsDevelopment()
            ? builder.Add(new HealthCheckRegistration(
                name,
                _ => new DisabledDependencyHealthCheck(environment.EnvironmentName),
                HealthStatus.Healthy,
                tags: [],
                CheckTimeout))
            : builder.AddDependency<TCheck>(name, HealthStatus.Degraded, []);

    private static IHealthChecksBuilder AddDependency<TCheck>(
        this IHealthChecksBuilder builder,
        string name,
        HealthStatus failureStatus,
        string[] tags)
        where TCheck : class, IHealthCheck
        => builder.Add(new HealthCheckRegistration(
            name,
            serviceProvider => ActivatorUtilities.CreateInstance<TCheck>(serviceProvider),
            failureStatus,
            tags,
            CheckTimeout));
}
