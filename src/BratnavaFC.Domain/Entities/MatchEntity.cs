using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public class MatchEntity : BaseEntity
{
    private MatchEntity() { } // EF

    public MatchEntity(Guid groupId, DateTime playedAt, string placeName)
    {
        if (groupId == Guid.Empty) throw new InvalidOperationException("GroupId e obrigatorio.");

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
    public List<GoalEntity> Goals { get; private set; } = [];

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
            throw new InvalidOperationException("GroupId e obrigatorio.");

        if (groupIdFromRequest != GroupId)
            throw new InvalidOperationException("GroupId informado nao pertence a esta partida.");

        if (dtoId.HasValue && dtoId.Value != matchIdFromRoute)
            throw new InvalidOperationException("Id do payload nao bate com o Id da rota.");

        if (string.IsNullOrWhiteSpace(placeName))
            throw new InvalidOperationException("Local da partida e obrigatorio.");

        PlayedAt = playedAt;
        PlaceName = placeName;
    }

    public void EnsureCanDelete()
    {
        if (Status == MatchStatus.Finalized)
            throw new InvalidOperationException("Partida ja Finalizada. Nao e possivel excluir.");
    }

    public void AcceptInvite(Guid playerId)
    {
        EnsureStatus(MatchStatus.Created, "So e possivel aceitar convite quando a partida esta Criada.");
        if (playerId == Guid.Empty) throw new InvalidOperationException("PlayerId e obrigatorio.");

        var mp = FindMatchPlayer(playerId);
        mp.InviteResponse = InviteResponse.Accepted;
    }

    public void RejectInvite(Guid playerId)
    {
        EnsureStatus(MatchStatus.Created, "So e possivel recusar convite quando a partida esta Criada.");
        if (playerId == Guid.Empty) throw new InvalidOperationException("PlayerId e obrigatorio.");

        var mp = FindMatchPlayer(playerId);
        mp.InviteResponse = InviteResponse.Rejected;
    }

    public void Start()
    {
        EnsureStatus(MatchStatus.Created, "A partida so pode ser iniciada se estiver Criada.");

        var hasTeamA = Players.Any(p => p.Team == 1);
        var hasTeamB = Players.Any(p => p.Team == 2);

        if (!hasTeamA || !hasTeamB)
            throw new InvalidOperationException("Nao e possivel iniciar a partida sem os times estarem definidos.");

        Status = MatchStatus.Started;
    }

    public void End()
    {
        EnsureStatus(MatchStatus.Started, "A partida so pode ser encerrada se estiver Iniciada.");
        Status = MatchStatus.Ended;
    }

    public void SetScore(int teamAGoals, int teamBGoals)
    {
        EnsureStatus(MatchStatus.Ended, "So e possivel setar placar quando a partida esta Encerrada.");

        if (teamAGoals < 0 || teamBGoals < 0)
            throw new InvalidOperationException("Placar nao pode ser negativo.");

        TeamAGoals = teamAGoals;
        TeamBGoals = teamBGoals;
    }

    public void SetTeamColors(Guid? teamAColorId, Guid? teamBColorId)
    {
        EnsureStatus(MatchStatus.Created, "So e possivel setar cores quando a partida esta Criada.");

        if (teamAColorId.HasValue && teamBColorId.HasValue && teamAColorId.Value == teamBColorId.Value)
            throw new InvalidOperationException("Os dois times nao podem possuir a mesma cor.");

        TeamAColorId = teamAColorId;
        TeamBColorId = teamBColorId;
    }

    public void SetTeamColorsRandomly(IReadOnlyList<TeamColorEntity> colors)
    {
        EnsureStatus(MatchStatus.Created, "So e possivel sortear cores quando a partida esta Criada.");

        if (colors == null || colors.Count == 0)
            throw new InvalidOperationException("Nao ha cores cadastradas para sortear.");

        var rng = Random.Shared;
        var shuffled = colors.OrderBy(_ => rng.Next()).ToList();

        var a = shuffled[0].Id;
        var b = shuffled.Count > 1 ? shuffled[1].Id : shuffled[0].Id;

        TeamAColorId = a;
        TeamBColorId = b;
    }

    public VoteEntity CreateVote(Guid voterMatchPlayerId, Guid votedMatchPlayerId)
    {
        EnsureStatus(MatchStatus.Ended, "So e possivel votar no MVP quando a partida esta Encerrada.");

        if (voterMatchPlayerId == Guid.Empty || votedMatchPlayerId == Guid.Empty)
            throw new InvalidOperationException("O jogador que votou e o votado sao obrigatorios.");

        if (voterMatchPlayerId == votedMatchPlayerId)
            throw new InvalidOperationException("O jogador nao pode votar em si mesmo.");

        var voter = Players.FirstOrDefault(p => p.Id == voterMatchPlayerId)
            ?? throw new InvalidOperationException("Apenas jogadores da partida podem votar.");

        if (Votes.Any(v => v.VoterId == voterMatchPlayerId))
            throw new InvalidOperationException("Esse jogador ja votou.");

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
        EnsureStatus(MatchStatus.Ended, "A partida so pode ser finalizada se estiver Encerrada.");

        if (!TeamAGoals.HasValue || !TeamBGoals.HasValue)
            throw new InvalidOperationException("Para finalizar a partida, o placar deve estar definido.");

        foreach (var p in Players)
            p.RevokeMvp();

        var winnerMatchPlayerId = Votes
            .Where(v => v.VotedForId != Guid.Empty)
            .GroupBy(v => v.VotedForId)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        if (winnerMatchPlayerId != Guid.Empty)
        {
            var winner = Players.FirstOrDefault(p => p.Id == winnerMatchPlayerId);
            winner?.SetMvp();
        }

        Status = MatchStatus.Finalized;
    }

    public void AddPlayer(MatchPlayerEntity matchPlayer, PlayerEntity playerEntity)
    {
        ArgumentNullException.ThrowIfNull(matchPlayer);
        ArgumentNullException.ThrowIfNull(playerEntity);

        EnsureStatus(MatchStatus.Created, "So e possivel adicionar/jogar convites quando a partida esta Criada.");

        if (playerEntity.GroupId != GroupId)
            throw new InvalidOperationException("Player nao pertence ao mesmo Group da partida.");

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
            throw new InvalidOperationException("Partida ja Finalizada. Nao e possivel atualizar seus dados.");
    }

    private void EnsureStatus(MatchStatus required, string message)
    {
        if (Status != required)
            throw new InvalidOperationException(message);
    }

    private MatchPlayerEntity FindMatchPlayer(Guid playerIdOrMatchPlayerId)
    {
        var mp = Players.FirstOrDefault(p => p.Id == playerIdOrMatchPlayerId || p.PlayerId == playerIdOrMatchPlayerId);
        if (mp == null) throw new InvalidOperationException("Jogador nao encontrado nesta partida.");
        return mp;
    }

    public void AssignTeams(IReadOnlyCollection<Guid> teamAPlayerIds, IReadOnlyCollection<Guid> teamBPlayerIds)
    {
        EnsureStatus(MatchStatus.Created, "So e possivel atribuir times quando a partida esta Criada.");

        teamAPlayerIds ??= Array.Empty<Guid>();
        teamBPlayerIds ??= Array.Empty<Guid>();

        if (teamAPlayerIds.Count == 0 || teamBPlayerIds.Count == 0)
            throw new InvalidOperationException("Os dois times devem ter ao menos 1 jogador.");

        if (teamAPlayerIds.Count != teamAPlayerIds.Distinct().Count())
            throw new InvalidOperationException("Time A contem IDs repetidos.");

        if (teamBPlayerIds.Count != teamBPlayerIds.Distinct().Count())
            throw new InvalidOperationException("Time B contem IDs repetidos.");

        var both = teamAPlayerIds.Intersect(teamBPlayerIds).ToList();
        if (both.Count > 0)
            throw new InvalidOperationException("Ha jogadores atribuidos aos dois times ao mesmo tempo.");

        var playersByPlayerId = Players.ToDictionary(p => p.PlayerId, p => p);

        var requested = teamAPlayerIds.Concat(teamBPlayerIds).ToList();

        var missing = requested.Where(id => !playersByPlayerId.ContainsKey(id)).Distinct().ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException("Ha jogadores que nao pertencem a esta partida.");

        var notAccepted = requested
            .Select(id => playersByPlayerId[id])
            .Where(mp => mp.InviteResponse != InviteResponse.Accepted)
            .ToList();

        if (notAccepted.Count > 0)
            throw new InvalidOperationException("Ha jogadores que ainda nao aceitaram o convite.");

        foreach (var mp in Players)
            mp.SetTeam(0);

        foreach (var playerId in teamAPlayerIds)
            playersByPlayerId[playerId].SetTeam(1);

        foreach (var playerId in teamBPlayerIds)
            playersByPlayerId[playerId].SetTeam(2);
    }

    public void SwapPlayers(Guid matchPlayerAId, Guid matchPlayerBId)
    {
        EnsureStatus(MatchStatus.Created, "So e possivel trocar jogadores quando a partida esta Criada.");

        if (matchPlayerAId == Guid.Empty || matchPlayerBId == Guid.Empty)
            throw new InvalidOperationException("Os dois jogadores sao obrigatorios.");

        if (matchPlayerAId == matchPlayerBId)
            throw new InvalidOperationException("Nao e possivel trocar o mesmo jogador.");

        var mpA = Players.FirstOrDefault(p => p.Id == matchPlayerAId);
        var mpB = Players.FirstOrDefault(p => p.Id == matchPlayerBId);

        if (mpA == null || mpB == null)
            throw new InvalidOperationException("Jogador nao encontrado nesta partida.");

        if (mpA.Team == 0 || mpB.Team == 0)
            throw new InvalidOperationException("Nao e possivel trocar jogadores nao atribuidos a times.");

        if (mpA.Team == mpB.Team)
            throw new InvalidOperationException("Os dois jogadores estao no mesmo time.");

        var teamA = mpA.Team;
        mpA.SetTeam(mpB.Team);
        mpB.SetTeam(teamA);
    }

    public void AddGoalByMatchPlayer(Guid scorerMatchPlayerId, Guid? assistMatchPlayerId, int? timeSeconds)
    {
        if (Status != MatchStatus.Started && Status != MatchStatus.Ended)
            throw new InvalidOperationException("So e possivel registrar gols quando a partida esta Iniciada ou Encerrada.");

        if (scorerMatchPlayerId == Guid.Empty)
            throw new InvalidOperationException("MatchPlayerId do gol e obrigatorio.");

        if (timeSeconds.HasValue && timeSeconds.Value < 0)
            throw new InvalidOperationException("Tempo do gol nao pode ser negativo.");

        var scorerMp = Players.FirstOrDefault(p => p.Id == scorerMatchPlayerId);
        if (scorerMp is null)
            throw new InvalidOperationException("O jogador do gol nao pertence a esta partida.");

        if (scorerMp.Team == 0)
            throw new InvalidOperationException("Nao e possivel registrar gol de jogador nao atribuido a um time.");

        MatchPlayerEntity? assistMp = null;

        if (assistMatchPlayerId.HasValue)
        {
            if (assistMatchPlayerId.Value == Guid.Empty)
                throw new InvalidOperationException("AssistMatchPlayerId invalido.");

            if (assistMatchPlayerId.Value == scorerMatchPlayerId)
                throw new InvalidOperationException("Assistente nao pode ser o mesmo jogador do gol.");

            assistMp = Players.FirstOrDefault(p => p.Id == assistMatchPlayerId.Value);
            if (assistMp is null)
                throw new InvalidOperationException("O jogador da assistencia nao pertence a esta partida.");

            if (assistMp.Team == 0)
                throw new InvalidOperationException("Nao e possivel registrar assistencia de jogador nao atribuido a um time.");

            if (assistMp.Team != scorerMp.Team)
                throw new InvalidOperationException("A assistencia so pode ser de um jogador do mesmo time do autor do gol.");
        }

        var goal = new GoalEntity(
            matchId: Id,
            groupId: GroupId,
            scorerMatchPlayerId: scorerMp.Id,
            assistMatchPlayerId: assistMp?.Id,
            timeSeconds: timeSeconds);

        Goals.Add(goal);

        RecalculateScoreFromGoals();
    }

    public void RemoveGoal(Guid goalId)
    {
        if (Status == MatchStatus.Finalized)
            throw new InvalidOperationException("Partida finalizada. Nao e possivel remover gols.");

        var idx = Goals.FindIndex(g => g.Id == goalId);
        if (idx < 0) return;

        Goals.RemoveAt(idx);

        RecalculateScoreFromGoals();
    }

    private void RecalculateScoreFromGoals()
    {
        var countA = 0;
        var countB = 0;

        var teamByMatchPlayerId = Players.ToDictionary(p => p.Id, p => p.Team);

        foreach (var g in Goals)
        {
            if (!teamByMatchPlayerId.TryGetValue(g.ScorerMatchPlayerId, out var team)) continue;

            if (team == 1) countA++;
            else if (team == 2) countB++;
        }

        TeamAGoals = countA;
        TeamBGoals = countB;
    }
}
