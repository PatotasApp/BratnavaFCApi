using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BratnavaFC.Api.HealthChecks;

/// <summary>
/// Registrado no lugar do check real quando a dependência é intencionalmente desabilitada
/// no ambiente. Reporta <see cref="HealthStatus.Healthy"/> para não degradar o status geral,
/// mas mantém a dependência visível no UI com a razão da ausência.
/// </summary>
public sealed class DisabledDependencyHealthCheck : IHealthCheck
{
    private readonly string _environmentName;

    public DisabledDependencyHealthCheck(string environmentName)
        => _environmentName = environmentName;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
        => Task.FromResult(HealthCheckResult.Healthy(
            $"Desabilitado no ambiente {_environmentName}."));
}
