using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public class MatchEntity : BaseEntity
{
    private MatchEntity() { } // EF

    public MatchEntity(Guid groupId, DateTime playedAt, string placeName)
    {
        if (groupId == Guid.Empty) throw new InvalidOperationException("GroupId é obrigatório.");

        GroupId = groupId;
        PlayedAt = playedAt;
        PlaceName = placeName ?? throw new ArgumentNullException(nameof(placeName));
        Status = MatchStatus.Created;
    }

    public Guid GroupId { get; private set; }
    public GroupEntity? Group { get; private set; }

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

    public void UpdateDetails(Guid groupIdFromRequest, DateTime playedAt, string placeName, Guid matchIdFromRoute, Guid? dtoId)
    {
        EnsureNotFinalized();

        if (groupIdFromRequest == Guid.Empty)
            throw new InvalidOperationException("GroupId é obrigatório.");

        if (groupIdFromRequest != GroupId)
            throw new InvalidOperationException("GroupId informado não pertence a esta partida.");

        if (dtoId.HasValue && dtoId.Value != matchIdFromRoute)
            throw new InvalidOperationException("Id do payload não bate com o Id da rota.");

        if (string.IsNullOrWhiteSpace(placeName))
            throw new InvalidOperationException("Local da partida é obrigatório.");

        PlayedAt = playedAt;
        PlaceName = placeName;
    }

    public void EnsureCanDelete()
    {
        if (Status == MatchStatus.Finalized)
            throw new InvalidOperationException("Partida já Finalizada. Não é possível excluir.");
    }

    public void AcceptInvite(Guid playerId)
    {
        EnsureStatus(MatchStatus.Created, "Só é possível aceitar convite quando a partida está Criada.");
        if (playerId == Guid.Empty) throw new InvalidOperationException("PlayerId é obrigatório.");

        var mp = FindMatchPlayer(playerId);
        mp.InviteResponse = InviteResponse.Accepted;
    }

    public void RejectInvite(Guid playerId)
    {
        EnsureStatus(MatchStatus.Created, "Só é possível recusar convite quando a partida está Criada.");
        if (playerId == Guid.Empty) throw new InvalidOperationException("PlayerId é obrigatório.");

        var mp = FindMatchPlayer(playerId);
        mp.InviteResponse = InviteResponse.Rejected;
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

    public void SetScore(int teamAGoals, int teamBGoals)
    {
        EnsureStatus(MatchStatus.Ended, "Só é possível setar placar quando a partida está Encerrada.");

        if (teamAGoals < 0 || teamBGoals < 0)
            throw new InvalidOperationException("Placar não pode ser negativo.");

        TeamAGoals = teamAGoals;
        TeamBGoals = teamBGoals;
    }

    public void SetTeamColors(Guid? teamAColorId, Guid? teamBColorId)
    {
        EnsureStatus(MatchStatus.Created, "Só é possível setar cores quando a partida está Criada.");

        TeamAColorId = teamAColorId;
        TeamBColorId = teamBColorId;
    }

    public void SetTeamColorsRandomly(IReadOnlyList<TeamColorEntity> colors)
    {
        EnsureStatus(MatchStatus.Created, "Só é possível sortear cores quando a partida está Criada.");

        if (colors == null || colors.Count == 0)
            throw new InvalidOperationException("Não há cores cadastradas para sortear.");

        var rng = Random.Shared;
        var shuffled = colors.OrderBy(_ => rng.Next()).ToList();

        var a = shuffled[0].Id;
        var b = shuffled.Count > 1 ? shuffled[1].Id : shuffled[0].Id;

        TeamAColorId = a;
        TeamBColorId = b;
    }

    public VoteEntity CreateVote(Guid voterMatchPlayerId, Guid votedMatchPlayerId)
    {
        EnsureStatus(MatchStatus.Ended, "Só é possível votar no MVP quando a partida está Encerrada.");

        if (voterMatchPlayerId == Guid.Empty || votedMatchPlayerId == Guid.Empty)
            throw new InvalidOperationException("O jogador que votou e o votado são obrigatórios.");

        if (voterMatchPlayerId == votedMatchPlayerId)
            throw new InvalidOperationException("O jogador não pode votar em si mesmo.");

        var voter = Players.FirstOrDefault(p => p.Id == voterMatchPlayerId)
            ?? throw new InvalidOperationException("Apenas jogadores da partida podem votar.");

        if (Votes.Any(v => v.VoterId == voterMatchPlayerId))
            throw new InvalidOperationException("Esse jogador já votou.");

        var votedFor = Players.FirstOrDefault(p => p.Id == votedMatchPlayerId)
            ?? throw new InvalidOperationException("Apenas jogadores que jogaram podem ser votados.");

        var vote = new VoteEntity(Id, voter.Id, votedFor.Id);

        voter.SetVotedFor(votedFor.Id);
        votedFor.AddReceivedVote(vote);

        return vote;
    }

    public MatchPlayerEntity? GetComputedMvp()
    {
        var top = Votes
            .Where(v => v.VotedForId != Guid.Empty)
            .GroupBy(v => v.VotedForId)
            .Select(g => new { PlayerId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();

        if (top == null || top.PlayerId == Guid.Empty) return null;

        return Players.FirstOrDefault(p => p.Id == top.PlayerId);
    }

    public void FinalizeByVotes()
    {
        EnsureStatus(MatchStatus.Ended, "A partida só pode ser finalizada se estiver Encerrada.");

        if (!TeamAGoals.HasValue || !TeamBGoals.HasValue)
            throw new InvalidOperationException("Para finalizar a partida, o placar deve estar definido.");

        var top = Votes
            .Where(v => v.VotedForId != Guid.Empty)
            .GroupBy(v => v.VotedForId)
            .Select(g => new { PlayerId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();

        Players.ForEach(p => p.RevokeMvp());

        if (top != null && top.PlayerId != Guid.Empty)
        {
            var winner = Players.FirstOrDefault(p => p.Id == top.PlayerId);
            winner?.SetMvp();
        }

        Status = MatchStatus.Finalized;
    }

    public void AddPlayer(MatchPlayerEntity matchPlayer, PlayerEntity playerEntity)
    {
        ArgumentNullException.ThrowIfNull(matchPlayer);
        ArgumentNullException.ThrowIfNull(playerEntity);

        EnsureStatus(MatchStatus.Created, "Só é possível adicionar/jogar convites quando a partida está Criada.");

        if (playerEntity.GroupId != GroupId)
            throw new InvalidOperationException("Player não pertence ao mesmo Group da partida.");

        if (Players.Any(p => p.PlayerId == playerEntity.Id))
            return;  

        matchPlayer.AssignToMatch(this);
        matchPlayer.AssignGroup(GroupId);
        matchPlayer.AssignToPlayer(playerEntity);

        matchPlayer.SetTeam(0);

        Players.Add(matchPlayer);
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

    private MatchPlayerEntity FindMatchPlayer(Guid playerIdOrMatchPlayerId)
    {
        var mp = Players.FirstOrDefault(p => p.Id == playerIdOrMatchPlayerId || p.PlayerId == playerIdOrMatchPlayerId);
        if (mp == null) throw new InvalidOperationException("Jogador não encontrado nesta partida.");
        return mp;
    }

    public void AssignTeams(
    IReadOnlyCollection<Guid> teamAPlayerIds,
    IReadOnlyCollection<Guid> teamBPlayerIds)
    {
        EnsureStatus(MatchStatus.Created, "Só é possível atribuir times quando a partida está Criada.");

        teamAPlayerIds ??= Array.Empty<Guid>();
        teamBPlayerIds ??= Array.Empty<Guid>();

        if (teamAPlayerIds.Count == 0 || teamBPlayerIds.Count == 0)
            throw new InvalidOperationException("Os dois times devem ter ao menos 1 jogador.");

        if (teamAPlayerIds.Count != teamAPlayerIds.Distinct().Count())
            throw new InvalidOperationException("Time A contém IDs repetidos.");

        if (teamBPlayerIds.Count != teamBPlayerIds.Distinct().Count())
            throw new InvalidOperationException("Time B contém IDs repetidos.");

        var both = teamAPlayerIds.Intersect(teamBPlayerIds).ToList();
        if (both.Count > 0)
            throw new InvalidOperationException("Há jogadores atribuídos aos dois times ao mesmo tempo.");

        var playersByPlayerId = Players.ToDictionary(p => p.PlayerId, p => p);

        var requested = teamAPlayerIds.Concat(teamBPlayerIds).ToList();

        var missing = requested.Where(id => !playersByPlayerId.ContainsKey(id)).Distinct().ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException("Há jogadores que não pertencem a esta partida.");

        var notAccepted = requested
            .Select(id => playersByPlayerId[id])
            .Where(mp => mp.InviteResponse != InviteResponse.Accepted)
            .ToList();

        if (notAccepted.Count > 0)
            throw new InvalidOperationException("Há jogadores que ainda não aceitaram o convite.");

        foreach (var mp in Players)
            mp.SetTeam(0);

        foreach (var playerId in teamAPlayerIds)
            playersByPlayerId[playerId].SetTeam(1);

        foreach (var playerId in teamBPlayerIds)
            playersByPlayerId[playerId].SetTeam(2);
    }

    public void SwapPlayers(Guid matchPlayerAId, Guid matchPlayerBId)
    {
        EnsureStatus(MatchStatus.Created, "Só é possível trocar jogadores quando a partida está Criada.");

        if (matchPlayerAId == Guid.Empty || matchPlayerBId == Guid.Empty)
            throw new InvalidOperationException("Os dois jogadores são obrigatórios.");

        if (matchPlayerAId == matchPlayerBId)
            throw new InvalidOperationException("Não é possível trocar o mesmo jogador.");

        var mpA = Players.FirstOrDefault(p => p.Id == matchPlayerAId);
        var mpB = Players.FirstOrDefault(p => p.Id == matchPlayerBId);

        if (mpA == null || mpB == null)
            throw new InvalidOperationException("Jogador não encontrado nesta partida.");

        if (mpA.Team == 0 || mpB.Team == 0)
            throw new InvalidOperationException("Não é possível trocar jogadores não atribuídos a times.");

        if (mpA.Team == mpB.Team)
            throw new InvalidOperationException("Os dois jogadores estão no mesmo time.");

        var teamA = mpA.Team;
        mpA.SetTeam(mpB.Team);
        mpB.SetTeam(teamA);
    }


}