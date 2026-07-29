using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BratnavaFC.Api.HealthChecks;

/// <summary>
/// Envolve o corpo de um check para que nenhuma exceção escape e o resultado sempre
/// respeite o <see cref="HealthCheckRegistration.FailureStatus"/> configurado — inclusive
/// no estouro do timeout do registro, que de outra forma seria reportado como Unhealthy.
/// </summary>
internal static class HealthCheckRunner
{
    public static async Task<HealthCheckResult> RunAsync(
        HealthCheckContext context,
        Func<CancellationToken, Task<HealthCheckResult>> body,
        CancellationToken ct)
    {
        try
        {
            return await body(ct);
        }
        catch (OperationCanceledException)
        {
            // O token recebido já vem linkado com o timeout do registro, então não há como
            // distinguir timeout de request abortado. Resposta de request abortado é descartada.
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "Timeout ao consultar a dependencia.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"{ex.GetType().Name}: {ex.Message}",
                ex);
        }
    }
}
