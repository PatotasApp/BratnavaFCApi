namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Voto de MVP. Com MVP por campeonato, GameId é nulo; com MVP por jogo, cada voto é de um jogo.
/// Jogadores referenciados pelo PlayerId (a participação está em ChampionshipPlayerEntity).
/// </summary>
public class ChampionshipVoteEntity : BaseEntity
{
    private ChampionshipVoteEntity() { } // EF

    public ChampionshipVoteEntity(Guid championshipId, Guid? gameId, Guid voterPlayerId, Guid votedPlayerId)
    {
        ChampionshipId = championshipId;
        GameId = gameId;
        VoterPlayerId = voterPlayerId;
        VotedPlayerId = votedPlayerId;
    }

    public Guid ChampionshipId { get; private set; }
    public Guid? GameId { get; private set; }
    public Guid VoterPlayerId { get; private set; }
    public Guid VotedPlayerId { get; private set; }
}
