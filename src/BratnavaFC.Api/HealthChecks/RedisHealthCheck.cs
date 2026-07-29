using BratnavaFC.Infrastructure.Redis;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace BratnavaFC.Api.HealthChecks;

public sealed class RedisHealthCheck : IHealthCheck
{
    public const string Name = "redis";

    private readonly IRedisConnectionProvider _redis;
    private readonly IConfiguration _configuration;

    public RedisHealthCheck(IRedisConnectionProvider redis, IConfiguration configuration)
    {
        _redis = redis;
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
        => HealthCheckRunner.RunAsync(context, async token =>
        {
            var streamKey = GetReplayStreamKey();
            var redis = await _redis.GetConnectionAsync(token);

            if (redis is null)
                return new HealthCheckResult(
                    context.Registration.FailureStatus,
                    "Redis indisponivel ou sem endpoints conectados. API segue online, mas replays/fila ficam indisponiveis.");

            // Valida a operacao real usada pelo sistema de replays, sem gravar nada no Redis.
            var ping = await redis.GetDatabase().PingAsync();
            var streamLength = await redis.GetDatabase().StreamLengthAsync(streamKey);

            return HealthCheckResult.Healthy(
                $"Ping Redis OK em {ping.TotalMilliseconds:n0} ms. Stream '{streamKey}' acessivel com {streamLength} eventos.");
        }, ct);

    private string GetReplayStreamKey()
        => Environment.GetEnvironmentVariable("REPLAY_EVENTS_STREAM")
           ?? _configuration["ReplayEvents:StreamKey"]
           ?? "replay_events";
}
