using System.Runtime.ConstrainedExecution;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public class MatchEntity : BaseEntity
{
    private MatchEntity() { } // EF

    public MatchEntity(DateTime playedAt, string placeName)
    {
        PlayedAt = playedAt;
        PlaceName = placeName ?? throw new ArgumentNullException(nameof(placeName));
        Status = MatchStatus.Created;
    }

    public DateTime PlayedAt { get; private set; }
    public int? TeamAGoals { get; private set; }
    public int? TeamBGoals { get; private set; }
    public string PlaceName { get; private set; } = string.Empty;

    public List<MatchPlayerEntity> Players { get; private set; } = [];
    public List<VoteEntity> Votes { get; private set; } = [];

    public MatchStatus Status { get; private set; }

    public IReadOnlyList<MatchPlayerEntity> TeamAPlayers => Players.Where(p => p.Team == 1).ToList();
    public IReadOnlyList<MatchPlayerEntity> TeamBPlayers => Players.Where(p => p.Team == 2).ToList();

    public Guid? TeamAColorId { get; private set; }
    public Guid? TeamBColorId { get; private set; }

    public TeamColorEntity? TeamAColor { get; private set; }
    public TeamColorEntity? TeamBColor { get; private set; }

    public void SetPlaceName(string placeName)
    {
        EnsureNotFinalized();
        PlaceName = placeName;
    }

    public void SetPlayedAt(DateTime playedAt)
    {
        EnsureNotFinalized();
        PlayedAt = playedAt;
    }

    public void SetScore(int homeGoals, int awayGoals)
    {
        EnsureStatus(MatchStatus.Ended, "Só é possível setar placar quando a partida está Encerrada.");
        TeamAGoals = homeGoals;
        TeamBGoals = awayGoals;
    }

    public void SetTeamColors(Guid? teamAColorId, Guid? teamBColorId)
    {
        EnsureStatus(MatchStatus.Created, "Só é possível setar cores quando a partida está Criada.");
        TeamAColorId = teamAColorId;
        TeamBColorId = teamBColorId;
    }

    public void AddPlayer(MatchPlayerEntity player)
    {
        ArgumentNullException.ThrowIfNull(player);
        EnsureStatus(MatchStatus.Created, "Só é possível adicionar/jogar convites quando a partida está Criada.");

        player.AssignToMatch(this);
        Players.Add(player);
    }

    public bool RemovePlayer(MatchPlayerEntity player)
    {
        ArgumentNullException.ThrowIfNull(player);
        EnsureStatus(MatchStatus.Created, "Só é possível remover jogadores quando a partida está Criada.");
        return Players.Remove(player);
    }

    public void Start()
    {
        EnsureStatus(MatchStatus.Created, "A partida só pode ser iniciada se estiver Criada.");
        Status = MatchStatus.Started;
    }

    public void End()
    {
        EnsureStatus(MatchStatus.Started, "A partida só pode ser encerrada se estiver Iniciada.");
        Status = MatchStatus.Ended;
    }

    public void FinalizeMatch()
    {
        EnsureStatus(MatchStatus.Ended, "A partida só pode ser finalizada se estiver Encerrada.");
        Status = MatchStatus.Finalized;
    }

    private void EnsureNotFinalized()
    {
        if (Status == MatchStatus.Finalized)
            throw new InvalidOperationException("Partida já Finalizada. Não é possível atualizar seus dados.");
    }

    private void EnsureStatus(MatchStatus required, string message)
    {
        if (Status != required)
            throw new InvalidOperationException(message);
    }
}