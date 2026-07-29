using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BratnavaFC.Api.HealthChecks;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    public const string Name = "database";

    private readonly AppDbContext _db;

    public DatabaseHealthCheck(AppDbContext db) => _db = db;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
        => HealthCheckRunner.RunAsync(context, async token =>
        {
            var ok = await _db.Database.CanConnectAsync(token);

            return ok
                ? HealthCheckResult.Healthy("Conexao com banco disponivel.")
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    "Nao foi possivel conectar ao banco.");
        }, ct);
}
