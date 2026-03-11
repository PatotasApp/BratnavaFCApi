using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public sealed class GoalEntity : BaseEntity
{
    private GoalEntity() { } // EF

    public GoalEntity(
        Guid matchId,
        Guid groupId,
        Guid scorerMatchPlayerId,
        Guid? assistMatchPlayerId,
        int? timeSeconds,
        bool isOwnGoal = false)
    {
        if (matchId == Guid.Empty) throw new InvalidOperationException("MatchId e obrigatorio.");
        if (groupId == Guid.Empty) throw new InvalidOperationException("GroupId e obrigatorio.");
        if (scorerMatchPlayerId == Guid.Empty) throw new InvalidOperationException("ScorerMatchPlayerId e obrigatorio.");
        if (timeSeconds.HasValue && timeSeconds.Value < 0) throw new InvalidOperationException("Tempo do gol nao pode ser negativo.");

        MatchId = matchId;
        GroupId = groupId;

        ScorerMatchPlayerId = scorerMatchPlayerId;
        AssistMatchPlayerId = assistMatchPlayerId;

        TimeSeconds = timeSeconds;
        IsOwnGoal = isOwnGoal;
    }

    public Guid MatchId { get; private set; }
    public MatchEntity? Match { get; private set; }

    public Guid GroupId { get; private set; }
    public GroupEntity? Group { get; private set; }

    public Guid ScorerMatchPlayerId { get; private set; }
    public MatchPlayerEntity? ScorerMatchPlayer { get; private set; }

    public Guid? AssistMatchPlayerId { get; private set; }
    public MatchPlayerEntity? AssistMatchPlayer { get; private set; }

    public int? TimeSeconds { get; private set; }

    /// <summary>
    /// Gol contra: o gol foi marcado pelo próprio jogador (ex: defleção na própria rede).
    /// O ponto vai para o time adversário do marcador.
    /// </summary>
    public bool IsOwnGoal { get; private set; }
}
