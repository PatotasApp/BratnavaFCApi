using System.Collections.Concurrent;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BranavaFC.Tests")]

namespace BratnavaFC.Application.Services;

public sealed class ReplayStreamConsumerService : BackgroundService
{
    private const string StreamKey = "replays:uploaded";
    private const string GroupName = "bratnava-api";
    private const int BatchSize   = 10;
    private const int BlockMs     = 30_000;
    private const int MaxAttempts = 3;
    private const int TtlDays     = 7;

    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReplayStreamConsumerService> _logger;
    private readonly string _consumerName;
    private readonly ConcurrentDictionary<string, int> _failureCounts = new();

    public ReplayStreamConsumerService(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        ILogger<ReplayStreamConsumerService> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _consumerName = $"bratnava-api-{Guid.NewGuid():N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureConsumerGroupAsync();

        _logger.LogInformation(
            "[ReplayStream] Consumidor iniciado. Stream={Stream} Group={Group} Consumer={Consumer}",
            StreamKey, GroupName, _consumerName);

        // Pendentes processados UMA VEZ só no startup (ex: restart da app)
        await DrainPendingAsync(stoppingToken);

        // Loop principal com BLOCK — só acorda quando chega mensagem nova
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNewMessagesBlockingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReplayStream] Erro no loop. Aguardando antes de retomar...");
                await Task.Delay(5_000, stoppingToken);
            }
        }

        _logger.LogInformation("[ReplayStream] Consumidor encerrado.");
    }

    // Drena mensagens pendentes (PEL) apenas no startup
    private async Task DrainPendingAsync(CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        StreamEntry[] entries;
        int total = 0;

        do
        {
            entries = await db.StreamReadGroupAsync(
                StreamKey, GroupName, _consumerName,
                position: "0",
                count: BatchSize,
                noAck: false);

            foreach (var entry in entries)
                await HandleEntryAsync(db, entry, ct);

            total += entries.Length;
        }
        while (entries.Length == BatchSize);

        if (total > 0)
            _logger.LogInformation("[ReplayStream] {Count} mensagens pendentes reprocessadas.", total);
    }

    // Usa XREADGROUP BLOCK — não polica, apenas acorda quando há mensagem nova
    private async Task ProcessNewMessagesBlockingAsync(CancellationToken ct)
    {
        var db = _redis.GetDatabase();

        // XREADGROUP GROUP <group> <consumer> COUNT <n> BLOCK <ms> STREAMS <key> >
        var result = await db.ExecuteAsync(
            "XREADGROUP",
            "GROUP", GroupName, _consumerName,
            "COUNT", BatchSize,
            "BLOCK", BlockMs,
            "STREAMS", StreamKey, ">");

        ct.ThrowIfCancellationRequested();

        // BLOCK retorna nil se expirou o timeout sem mensagens — normal, só volta ao loop
        if (result.IsNull) return;

        var entries = ParseBlockResult(result);
        foreach (var entry in entries)
            await HandleEntryAsync(db, entry, ct);
    }

    // Parseia o resultado raw do XREADGROUP BLOCK
    private static IEnumerable<StreamEntry> ParseBlockResult(RedisResult result)
    {
        // Formato: [ [streamKey, [ [id, [field, value, ...]], ... ]] ]
        var streams = (RedisResult[])result!;
        var streamData = (RedisResult[])streams[0];
        var messages = (RedisResult[])streamData[1];

        foreach (var msg in messages)
        {
            var msgParts = (RedisResult[])msg;
            var id = (string)msgParts[0]!;
            var fields = (RedisResult[])msgParts[1];

            var values = new List<NameValueEntry>();
            for (int i = 0; i < fields.Length; i += 2)
                values.Add(new NameValueEntry((string)fields[i]!, (string)fields[i + 1]!));

            yield return new StreamEntry(id, values.ToArray());
        }
    }

    internal async Task HandleEntryAsync(IDatabase db, StreamEntry entry, CancellationToken ct)
    {
        var entryId = entry.Id.ToString();

        try
        {
            var clip = ParseEntry(entry);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            context.ReplayClips.Add(clip);
            await context.SaveChangesAsync(ct);

            await db.StreamAcknowledgeAsync(StreamKey, GroupName, entry.Id);
            _failureCounts.TryRemove(entryId, out _);

            var minId = $"{DateTimeOffset.UtcNow.AddDays(-TtlDays).ToUnixTimeMilliseconds()}-0";
            await db.ExecuteAsync("XTRIM", StreamKey, "MINID", "~", minId);

            _logger.LogInformation(
                "[ReplayStream] Clip salvo. Id={Id} Match={Match} EventType={Type} Key={Key}",
                clip.Id, clip.MatchId, clip.EventType, clip.ObjectKey);
        }
        catch (Exception ex)
        {
            var attempts = _failureCounts.AddOrUpdate(entryId, 1, (_, count) => count + 1);

            if (attempts >= MaxAttempts)
            {
                await db.StreamAcknowledgeAsync(StreamKey, GroupName, entry.Id);
                _failureCounts.TryRemove(entryId, out _);
                _logger.LogError(ex, "[ReplayStream] Entry {EntryId} falhou {Attempts}x — descartada.", entryId, attempts);
            }
            else
            {
                _logger.LogWarning(ex,
                    "[ReplayStream] Entry {EntryId} falhou (tentativa {Attempt}/{Max}). Será reprocessada.",
                    entryId, attempts, MaxAttempts);
            }
        }
    }

    private static ReplayClipEntity ParseEntry(StreamEntry entry)
    {
        var f = entry.Values.ToDictionary(
            kv => kv.Name.ToString(),
            kv => kv.Value.ToString());

        var recordedAt = f.TryGetValue("event_time", out var et) && !string.IsNullOrEmpty(et)
            ? DateTimeOffset.Parse(et)
            : DateTimeOffset.Parse(f["created_at"]);

        return new ReplayClipEntity(
            groupId: Guid.Parse(f["group_id"]),
            matchId: Guid.Parse(f["match_id"]),
            bucketName: f["bucket_name"],
            objectKey: f["object_key"],
            contentType: f["content_type"],
            etag: f["etag"],
            recordedAt: recordedAt,
            eventType: ParseEventType(f["tipo"]));
    }

    private static MatchEventType ParseEventType(string tipo) => tipo.ToLowerInvariant() switch
    {
        "gol" => MatchEventType.Gol,
        "jogada" => MatchEventType.Jogada,
        _ => throw new ArgumentException($"Tipo desconhecido no stream: '{tipo}'")
    };

    private async Task EnsureConsumerGroupAsync()
    {
        var db = _redis.GetDatabase();
        try
        {
            await db.StreamCreateConsumerGroupAsync(StreamKey, GroupName, "$", createStream: true);
            _logger.LogInformation("[ReplayStream] Consumer group '{Group}' criado.", GroupName);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            _logger.LogInformation("[ReplayStream] Consumer group '{Group}' já existe.", GroupName);
        }
    }
}
