using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace BratnavaFC.Api.HealthChecks;

public sealed class HangfireHealthCheck : IHealthCheck
{
    public const string Name = "hangfire";

    private readonly IConfiguration _configuration;

    public HangfireHealthCheck(IConfiguration configuration) => _configuration = configuration;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
        => HealthCheckRunner.RunAsync(context, async token =>
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
                return new HealthCheckResult(
                    context.Registration.FailureStatus,
                    "DefaultConnection nao configurada.");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(token);

            await using var command = new NpgsqlCommand(
                "select to_regclass('hangfire.server') is not null",
                connection);

            var hasSchema = await command.ExecuteScalarAsync(token) as bool? ?? false;

            return hasSchema
                ? HealthCheckResult.Healthy("Storage do Hangfire disponivel.")
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    "Conexao OK, mas schema do Hangfire nao foi encontrado.");
        }, ct);
}
