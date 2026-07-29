using FirebaseAdmin;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BratnavaFC.Api.HealthChecks;

public sealed class FirebaseHealthCheck : IHealthCheck
{
    public const string Name = "firebase";

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
        => HealthCheckRunner.RunAsync(context, _ =>
        {
            var hasJson = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_JSON"));

            var result = IsFirebaseInitialized()
                ? HealthCheckResult.Healthy("Firebase Admin inicializado.")
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    hasJson
                        ? "Firebase Admin nao inicializado apesar de FIREBASE_SERVICE_ACCOUNT_JSON estar presente."
                        : "Firebase Admin nao inicializado e FIREBASE_SERVICE_ACCOUNT_JSON nao configurada.");

            return Task.FromResult(result);
        }, ct);

    private static bool IsFirebaseInitialized()
    {
        try
        {
            return FirebaseApp.DefaultInstance is not null;
        }
        catch
        {
            return false;
        }
    }
}
