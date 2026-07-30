using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BratnavaFC.Api.HealthChecks;

public sealed class OpenAiHealthCheck : IHealthCheck
{
    public const string Name = "openai";

    private readonly IConfiguration _configuration;

    public OpenAiHealthCheck(IConfiguration configuration) => _configuration = configuration;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
        => HealthCheckRunner.RunAsync(context, _ =>
        {
            var hasKey =
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")) ||
                !string.IsNullOrWhiteSpace(_configuration["OpenAI:ApiKey"]);

            var result = hasKey
                ? HealthCheckResult.Healthy("Chave configurada. Nenhuma chamada externa foi feita.")
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    "Chave nao configurada.");

            return Task.FromResult(result);
        }, ct);
}
