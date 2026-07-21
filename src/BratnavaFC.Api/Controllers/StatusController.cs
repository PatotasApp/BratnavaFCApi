using System.Diagnostics;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Infrastructure.Data;
using FirebaseAdmin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/status")]
public sealed class StatusController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IRedisConnectionProvider _redis;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _environment;

    public StatusController(
        AppDbContext db,
        IRedisConnectionProvider redis,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment environment)
    {
        _db = db;
        _redis = redis;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _environment = environment;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<SystemStatusDto>>> Get(CancellationToken ct)
    {
        var checks = new List<EnvironmentCheckDto>
        {
            await CheckDatabaseAsync(ct),
            await CheckHangfireAsync(ct),
            await CheckRedisAsync(ct),
            CheckReplayStream(),
            CheckFirebase(),
            CheckR2(),
            CheckOpenAi(),
            await CheckBrasilApiAsync(ct),
        };

        var overall = checks.Any(c => c.Status == "down")
            ? "down"
            : checks.Any(c => c.Status is "degraded" or "not_configured")
                ? "degraded"
                : "ok";

        var dto = new SystemStatusDto(
            overall,
            _environment.EnvironmentName,
            DateTimeOffset.UtcNow,
            checks);

        return Ok(new ApiResponse<SystemStatusDto>(
            overall != "down",
            dto,
            null,
            overall == "down" ? "Um ou mais ambientes estao indisponiveis." : null,
            []));
    }

    private async Task<EnvironmentCheckDto> CheckDatabaseAsync(CancellationToken ct)
    {
        return await MeasureAsync("Banco PostgreSQL", "database", async token =>
        {
            var ok = await _db.Database.CanConnectAsync(token);
            return ok
                ? ("ok", "Conexao com banco disponivel.")
                : ("down", "Nao foi possivel conectar ao banco.");
        }, ct);
    }

    private async Task<EnvironmentCheckDto> CheckHangfireAsync(CancellationToken ct)
    {
        return await MeasureAsync("Hangfire", "jobs", async token =>
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
                return ("not_configured", "DefaultConnection nao configurada.");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand("select to_regclass('hangfire.server') is not null", connection);
            var hasSchema = await command.ExecuteScalarAsync(token) as bool? ?? false;

            return hasSchema
                ? ("ok", "Storage do Hangfire disponivel.")
                : ("degraded", "Conexao OK, mas schema do Hangfire nao foi encontrado.");
        }, ct);
    }

    private async Task<EnvironmentCheckDto> CheckRedisAsync(CancellationToken ct)
    {
        return await MeasureAsync("Redis / Upstash", "cache-stream", async token =>
        {
            var redis = await _redis.GetConnectionAsync(token);
            if (redis is null)
                return ("down", "Redis indisponivel ou sem endpoints conectados.");

            var ping = await redis.GetDatabase().PingAsync();
            return ("ok", $"Ping Redis OK em {ping.TotalMilliseconds:n0} ms.");
        }, ct);
    }

    private EnvironmentCheckDto CheckReplayStream()
    {
        var streamKey =
            Environment.GetEnvironmentVariable("REPLAY_EVENTS_STREAM")
            ?? _configuration["ReplayEvents:StreamKey"]
            ?? "replay_events";

        return new EnvironmentCheckDto(
            "Replay events stream",
            "cache-stream",
            "ok",
            $"Stream Redis configurado como '{streamKey}'.",
            null,
            DateTimeOffset.UtcNow);
    }

    private EnvironmentCheckDto CheckFirebase()
    {
        var hasJson =
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_B64")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_JSON")) ||
            !string.IsNullOrWhiteSpace(_configuration["Firebase:ServiceAccountJson"]);
        var hasPath = !string.IsNullOrWhiteSpace(_configuration["Firebase:ServiceAccountPath"]);
        var initialized = IsFirebaseInitialized();

        return new EnvironmentCheckDto(
            "Firebase",
            "push",
            initialized ? "ok" : hasJson || hasPath ? "degraded" : "not_configured",
            initialized ? "Firebase Admin inicializado." : "Firebase Admin nao inicializado.",
            null,
            DateTimeOffset.UtcNow);
    }

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

    private EnvironmentCheckDto CheckR2()
    {
        var bucketName = Environment.GetEnvironmentVariable("CLOUDFLARE_R2_BUCKET_NAME") ?? "goal-replays";
        var required = new[]
        {
            "CLOUDFLARE_R2_ENDPOINT_URL",
            "CLOUDFLARE_R2_ACCESS_KEY_ID",
            "CLOUDFLARE_R2_SECRET_ACCESS_KEY",
        };

        var missing = required
            .Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            .ToArray();

        return new EnvironmentCheckDto(
            "Cloudflare R2",
            "storage",
            missing.Length == 0 ? "ok" : "not_configured",
            missing.Length == 0
                ? $"Variaveis obrigatorias configuradas. Bucket: {bucketName}."
                : $"Variaveis ausentes: {string.Join(", ", missing)}. Bucket: {bucketName}.",
            null,
            DateTimeOffset.UtcNow);
    }

    private EnvironmentCheckDto CheckOpenAi()
    {
        var hasKey =
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")) ||
            !string.IsNullOrWhiteSpace(_configuration["OpenAI:ApiKey"]);

        return new EnvironmentCheckDto(
            "OpenAI",
            "external-api",
            hasKey ? "ok" : "not_configured",
            hasKey ? "Chave configurada. Nenhuma chamada externa foi feita." : "Chave nao configurada.",
            null,
            DateTimeOffset.UtcNow);
    }

    private async Task<EnvironmentCheckDto> CheckBrasilApiAsync(CancellationToken ct)
    {
        return await MeasureAsync("Brasil API", "external-api", async token =>
        {
            var client = _httpClientFactory.CreateClient("BrasilApi");
            using var response = await client.GetAsync("api/feriados/v1/2026", token);
            return response.IsSuccessStatusCode
                ? ("ok", $"Brasil API respondeu HTTP {(int)response.StatusCode}.")
                : ("degraded", $"Brasil API respondeu HTTP {(int)response.StatusCode}.");
        }, ct);
    }

    private static async Task<EnvironmentCheckDto> MeasureAsync(
        string name,
        string kind,
        Func<CancellationToken, Task<(string Status, string Detail)>> action,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var (status, detail) = await action(timeout.Token);
            sw.Stop();
            return new EnvironmentCheckDto(name, kind, status, detail, sw.ElapsedMilliseconds, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new EnvironmentCheckDto(name, kind, "down", "Timeout apos 5 segundos.", sw.ElapsedMilliseconds, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new EnvironmentCheckDto(name, kind, "down", $"{ex.GetType().Name}: {ex.Message}", sw.ElapsedMilliseconds, DateTimeOffset.UtcNow);
        }
    }
}

public sealed record SystemStatusDto(
    string Overall,
    string Environment,
    DateTimeOffset CheckedAt,
    IReadOnlyList<EnvironmentCheckDto> Checks);

public sealed record EnvironmentCheckDto(
    string Name,
    string Kind,
    string Status,
    string Detail,
    long? DurationMs,
    DateTimeOffset CheckedAt);
