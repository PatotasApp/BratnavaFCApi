using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Lance marcado para as câmeras num jogo de campeonato ("gol do time A/B" ou "jogada").
/// Guarda de qual campeonato e de qual jogo era e quando aconteceu, para depois puxar a
/// gravação. O pedido de recorte segue pelo mesmo canal das partidas (matchId = id do jogo).
/// </summary>
public class ChampionshipGameEventEntity : BaseEntity
{
    private ChampionshipGameEventEntity() { } // EF

    public ChampionshipGameEventEntity(
        Guid championshipId, Guid gameId, MatchEventType type, Guid? teamId,
        DateTime eventTimeUtc, int? elapsedSeconds, string? queueId, Guid? createdByUserId)
    {
        ChampionshipId = championshipId;
        GameId = gameId;
        Type = type;
        TeamId = teamId;
        EventTimeUtc = DateTime.SpecifyKind(eventTimeUtc, DateTimeKind.Utc);
        ElapsedSeconds = elapsedSeconds;
        QueueId = queueId;
        CreatedByUserId = createdByUserId;
    }

    public Guid ChampionshipId { get; private set; }
    public Guid GameId { get; private set; }
    public MatchEventType Type { get; private set; }

    /// <summary>Time do gol (GolTimeA/GolTimeB); nulo em "jogada".</summary>
    public Guid? TeamId { get; private set; }

    public DateTime EventTimeUtc { get; private set; }

    /// <summary>Segundos desde o início do jogo.</summary>
    public int? ElapsedSeconds { get; private set; }

    /// <summary>Id devolvido pelo publicador de eventos (fila/outbox das câmeras).</summary>
    public string? QueueId { get; private set; }

    public Guid? CreatedByUserId { get; private set; }
}
