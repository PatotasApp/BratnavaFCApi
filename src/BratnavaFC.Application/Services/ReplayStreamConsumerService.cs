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
    private const int BatchSize = 10;
    private const int PollDelayMs = 5_000;
    private const int MaxAttempts = 3;

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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Processar pendentes primeiro (entregues mas não ackados — ex: restart)
                await ProcessBatchAsync("0", stoppingToken);

                // Ler novas mensagens
                await ProcessBatchAsync(">", stoppingToken);

                await Task.Delay(PollDelayMs, stoppingToken);
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

    // Lê um batch de mensagens até esgotar entradas na posição dada
    private async Task ProcessBatchAsync(string position, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        StreamEntry[] entries;

        do
        {
            entries = await db.StreamReadGroupAsync(
                StreamKey, GroupName, _consumerName,
                position: position,
                count: BatchSize,
                noAck: false);

            foreach (var entry in entries)
                await HandleEntryAsync(db, entry, ct);
        }
        while (entries.Length == BatchSize);
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

                _logger.LogError(ex,
                    "[ReplayStream] Entry {EntryId} falhou {Attempts}x — descartada.",
                    entryId, attempts);
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

        return new ReplayClipEntity(
            groupId: Guid.Parse(f["group_id"]),
            matchId: Guid.Parse(f["match_id"]),
            bucketName: f["bucket_name"],
            objectKey: f["object_key"],
            contentType: f["content_type"],
            etag: f["etag"],
            uploadedAt: DateTimeOffset.Parse(f["created_at"]),
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
            // Grupo já existe — normal em restarts
            _logger.LogInformation("[ReplayStream] Consumer group '{Group}' já existe.", GroupName);
        }
    }
}
