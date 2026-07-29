using BratnavaFC.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Infrastructure.Redis;

/// <summary>
/// Registrado quando o Redis está desabilitado no ambiente. Não grava no stream nem no
/// outbox do banco — só devolve um id sintético para o chamador seguir seu fluxo normal.
/// </summary>
public sealed class NoOpMatchEventPublisher : IMatchEventPublisher
{
    private readonly ILogger<NoOpMatchEventPublisher> _logger;
    private readonly string _environmentName;

    public NoOpMatchEventPublisher(
        ILogger<NoOpMatchEventPublisher> logger,
        string environmentName)
    {
        _logger = logger;
        _environmentName = environmentName;
    }

    public Task<string> PublishAsync(
        Guid groupId,
        Guid matchId,
        MatchEventType type,
        int secondsBeforeStart,
        int durationSeconds,
        DateTimeOffset? eventTime = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[ReplayEvents] Publicacao ignorada porque o Redis esta desabilitado no ambiente {Environment}. Group={GroupId} Match={MatchId} Type={Type}",
            _environmentName,
            groupId,
            matchId,
            type);

        return Task.FromResult($"dev:{Guid.NewGuid()}");
    }
}
