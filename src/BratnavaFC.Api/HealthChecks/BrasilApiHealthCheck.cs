using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BratnavaFC.Api.HealthChecks;

public sealed class BrasilApiHealthCheck : IHealthCheck
{
    public const string Name = "brasil-api";

    private readonly IHttpClientFactory _httpClientFactory;

    public BrasilApiHealthCheck(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
        => HealthCheckRunner.RunAsync(context, async token =>
        {
            var client = _httpClientFactory.CreateClient("BrasilApi");
            using var response = await client.GetAsync($"api/feriados/v1/{DateTime.UtcNow.Year}", token);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"Brasil API respondeu HTTP {(int)response.StatusCode}.")
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"Brasil API respondeu HTTP {(int)response.StatusCode}.");
        }, ct);
}
