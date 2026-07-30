using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BratnavaFC.Api.HealthChecks;

public sealed class CloudflareR2HealthCheck : IHealthCheck
{
    public const string Name = "cloudflare-r2";

    private static readonly string[] RequiredVariables =
    [
        "CLOUDFLARE_R2_ENDPOINT_URL",
        "CLOUDFLARE_R2_ACCESS_KEY_ID",
        "CLOUDFLARE_R2_SECRET_ACCESS_KEY",
    ];

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
        => HealthCheckRunner.RunAsync(context, _ =>
        {
            var bucketName = Environment.GetEnvironmentVariable("CLOUDFLARE_R2_BUCKET_NAME") ?? "goal-replays";

            var missing = RequiredVariables
                .Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
                .ToArray();

            var result = missing.Length == 0
                ? HealthCheckResult.Healthy($"Variaveis obrigatorias configuradas. Bucket: {bucketName}.")
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"Variaveis ausentes: {string.Join(", ", missing)}. Bucket: {bucketName}.");

            return Task.FromResult(result);
        }, ct);
}
