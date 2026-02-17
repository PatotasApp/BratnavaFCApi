using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Domain.Entities;

public class GoalEntity : BaseEntity
{
    private GoalEntity() { } // EF

    public GoalEntity(Guid matchId, Guid groupId, Guid scorerPlayerId, Guid? assistPlayerId, int? timeSeconds)
    {
        if (matchId == Guid.Empty) throw new InvalidOperationException("MatchId e obrigatorio.");
        if (groupId == Guid.Empty) throw new InvalidOperationException("GroupId e obrigatorio.");
        if (scorerPlayerId == Guid.Empty) throw new InvalidOperationException("ScorerPlayerId e obrigatorio.");
        if (assistPlayerId.HasValue && assistPlayerId.Value == Guid.Empty)
            throw new InvalidOperationException("AssistPlayerId invalido.");

        if (assistPlayerId.HasValue && assistPlayerId.Value == scorerPlayerId)
            throw new InvalidOperationException("Assistente nao pode ser o mesmo jogador do gol.");

        if (timeSeconds.HasValue && timeSeconds.Value < 0)
            throw new InvalidOperationException("Tempo do gol nao pode ser negativo.");

        MatchId = matchId;
        GroupId = groupId;
        ScorerPlayerId = scorerPlayerId;
        AssistPlayerId = assistPlayerId;
        TimeSeconds = timeSeconds;
    }

    public Guid MatchId { get; private set; }
    public MatchEntity? Match { get; private set; }

    public Guid GroupId { get; private set; }
    public GroupEntity? Group { get; private set; }

    public Guid ScorerPlayerId { get; private set; }
    public PlayerEntity? ScorerPlayer { get; private set; }

    public Guid? AssistPlayerId { get; private set; }
    public PlayerEntity? AssistPlayer { get; private set; }

    /// <summary>Total de segundos (ex: 90 = 01:30). Null = sem tempo informado.</summary>
    public int? TimeSeconds { get; private set; }
}
