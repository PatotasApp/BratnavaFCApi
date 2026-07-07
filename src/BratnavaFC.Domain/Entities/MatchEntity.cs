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

    /// <summary>Votação vinculada a esta partida (opcional). Null quando não há vínculo.</summary>
    public Guid? LinkedPollId { get; private set; }
    public PollEntity? LinkedPoll { get; private set; }

    public void SetLinkedPoll(Guid? pollId)
    {
        LinkedPollId = pollId;
        UpdateDate   = DateTime.UtcNow;
    }

    public void OpenAcceptation()
    {
        EnsureStatus(MatchStatus.Created, "So e possivel abrir acceptation quando a partida esta Created.");
        Status = MatchStatus.Acceptation;
    }

    public void GoToMatchMaking()
    {
        EnsureStatus(MatchStatus.Acceptation, "So e possivel ir para MatchMaking quando a partida esta em Acceptation.");

        var acceptedCount = Players.Count(p => p.InviteResponse == InviteResponse.Accepted);
        if (acceptedCount < 2)
            throw new InvalidOperationException("Precisa de ao menos 2 jogadores aceitos para gerar times.");

        Status = MatchStatus.MatchMaking;
    }

    public void GoToPostGame()
    {
        EnsureStatus(MatchStatus.Ended, "So e possivel ir para PostGame quando a partida esta Ended.");
        Status = MatchStatus.PostGame;
    }

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
        EnsureStatus(MatchStatus.Acceptation, "So e possivel aceitar convite quando a partida esta em Acceptation.");
        if (playerId == Guid.Empty) throw new InvalidOperationException("PlayerId e obrigatorio.");

        var mp = FindMatchPlayer(playerId);
        mp.AcceptInvite();
    }

    public void RejectInvite(Guid playerId)
    {
        EnsureStatus(MatchStatus.Acceptation, "So e possivel recusar convite quando a partida esta em Acceptation.");
        if (playerId == Guid.Empty) throw new InvalidOperationException("PlayerId e obrigatorio.");

        var mp = FindMatchPlayer(playerId);
        mp.RejectInvite();
    }

    /// <summary>Momento real em que a partida foi iniciada (UTC). Null para partidas antigas.</summary>
    public DateTime? ActualStartTime { get; private set; }

    public void Start()
    {
        EnsureStatus(MatchStatus.MatchMaking, "A partida so pode ser iniciada se estiver em MatchMaking.");

        var hasTeamA = Players.Any(p => p.Team == 1);
        var hasTeamB = Players.Any(p => p.Team == 2);

        if (!hasTeamA || !hasTeamB)
            throw new InvalidOperationException("Nao e possivel iniciar a partida sem os times estarem definidos.");

        ActualStartTime = DateTime.UtcNow;
        Status = MatchStatus.Started;
    }

    public void End()
    {
        EnsureStatus(MatchStatus.Started, "A partida so pode ser encerrada se estiver em Started.");
        Status = MatchStatus.Ended;
    }

    public void SetScore(int teamAGoals, int teamBGoals)
    {
        EnsureStatus(MatchStatus.PostGame, "So e possivel setar placar quando a partida esta em PostGame.");

        if (teamAGoals < 0 || teamBGoals < 0)
            throw new InvalidOperationException("Placar nao pode ser negativo.");

        TeamAGoals = teamAGoals;
        TeamBGoals = teamBGoals;
    }

    public void SetTeamColors(Guid? teamAColorId, Guid? teamBColorId)
    {
        EnsureStatus(MatchStatus.MatchMaking, "So e possivel setar cores quando a partida esta em MatchMaking.");

        if (teamAColorId.HasValue && teamBColorId.HasValue && teamAColorId.Value == teamBColorId.Value)
            throw new InvalidOperationException("Os dois times nao podem possuir a mesma cor.");

        TeamAColorId = teamAColorId;
        TeamBColorId = teamBColorId;
    }

    public void SetTeamColorsRandomly(IReadOnlyList<TeamColorEntity> colors)
    {
        EnsureStatus(MatchStatus.MatchMaking, "So e possivel setar cores quando a partida esta em MatchMaking.");

        if (colors == null || colors.Count == 0)
            throw new InvalidOperationException("Nao ha cores cadastradas para sortear.");

        var rng = Random.Shared;
        var shuffled = colors.OrderBy(_ => rng.Next()).ToList();

        var a = shuffled[0].Id;
        var b = shuffled.Count > 1 ? shuffled[1].Id : shuffled[0].Id;

        if (a == b)
            throw new InvalidOperationException("Nao foi possivel sortear duas cores distintas.");

        TeamAColorId = a;
        TeamBColorId = b;
    }

    public VoteEntity CreateVote(Guid voterMatchPlayerId, Guid votedMatchPlayerId)
    {
        EnsureStatus(MatchStatus.PostGame, "So e possivel votar no MVP quando a partida esta em PostGame.");

        if (voterMatchPlayerId == Guid.Empty || votedMatchPlayerId == Guid.Empty)
            throw new InvalidOperationException("O jogador que votou e o votado sao obrigatorios.");

        if (voterMatchPlayerId == votedMatchPlayerId)
            throw new InvalidOperationException("O jogador nao pode votar em si mesmo.");

        var voter = Players.FirstOrDefault(p => p.Id == voterMatchPlayerId)
            ?? throw new InvalidOperationException("Apenas jogadores da partida podem votar.");

        if (voter.Player?.IsGuest == true)
            throw new InvalidOperationException("Convidados não podem votar no MVP.");

        if (Votes.Any(v => v.VoterId == voterMatchPlayerId))
            throw new InvalidOperationException("Esse jogador ja votou.");

        var votedFor = Players.FirstOrDefault(p => p.Id == votedMatchPlayerId)
            ?? throw new InvalidOperationException("Apenas jogadores que jogaram podem ser votados.");

        var vote = new VoteEntity(Id, voter.Id, votedFor.Id);

        voter.SetVotedFor(votedFor.Id);
        votedFor.AddReceivedVote(vote);
        Votes.Add(vote); // mantém a coleção em memória atualizada para AutoSetMvpIfAllVoted

        return vote;
    }

    public IReadOnlyList<MatchPlayerEntity> GetComputedMvps()
    {
        var groups = Votes
            .Where(v => v.VotedForId != Guid.Empty)
            .GroupBy(v => v.VotedForId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToList();

        if (groups.Count == 0) return Array.Empty<MatchPlayerEntity>();

        var maxCount = groups.Max(x => x.Count);
        var winnerIds = groups.Where(x => x.Count == maxCount).Select(x => x.Id).ToHashSet();
        return Players.Where(p => winnerIds.Contains(p.Id)).ToList();
    }

    public MatchPlayerEntity? GetComputedMvp() => GetComputedMvps().FirstOrDefault();

    /// <summary>
    /// Se todos os jogadores não-convidados já votaram, persiste o MVP automaticamente.
    /// Deve ser chamado após salvar o voto. Retorna true se o MVP foi definido.
    /// Requer que a navegação Players.Player esteja carregada.
    /// </summary>
    public bool AutoSetMvpIfAllVoted(
        Domain.Enums.MvpTieRule tieRule = Domain.Enums.MvpTieRule.AllMvp,
        int tieMaxPlayers = 2)
    {
        var eligible = Players.Where(p => p.Player?.IsGuest != true && (p.Team == 1 || p.Team == 2)).ToList();
        if (eligible.Count == 0) return false;

        var voterIds = Votes.Select(v => v.VoterId).ToHashSet();
        if (!eligible.All(p => voterIds.Contains(p.Id))) return false;

        // Revoga MVP anterior antes de definir o(s) novo(s)
        foreach (var p in Players)
            p.RevokeMvp();

        ApplyMvpTieRule(tieRule, tieMaxPlayers);

        return true;
    }

    public void FinalizeByVotes(
        Domain.Enums.MvpTieRule tieRule = Domain.Enums.MvpTieRule.AllMvp,
        int tieMaxPlayers = 2)
    {
        EnsureStatus(MatchStatus.PostGame, "A partida so pode ser finalizada se estiver em PostGame.");

        if ((!TeamAGoals.HasValue || !TeamBGoals.HasValue) && Goals.Count > 0)
            RecalculateScoreFromGoals();

        if (!TeamAGoals.HasValue || !TeamBGoals.HasValue)
            throw new InvalidOperationException("Para finalizar a partida, o placar deve estar definido (placar ou gols).");

        foreach (var p in Players)
            p.RevokeMvp();

        ApplyMvpTieRule(tieRule, tieMaxPlayers);

        Status = MatchStatus.Finalized;
    }

    /// <summary>
    /// Recalcula e persiste os MVPs a partir dos votos, aplicando a regra de empate configurada.
    /// Pode ser chamado em partidas PostGame ou Finalized (por exemplo, para corrigir um MVP não atribuído).
    /// </summary>
    public void ReapplyMvpTieRule(Domain.Enums.MvpTieRule tieRule, int tieMaxPlayers)
    {
        foreach (var p in Players)
            p.RevokeMvp();

        ApplyMvpTieRule(tieRule, tieMaxPlayers);
    }

    /// <summary>
    /// Calcula os MVPs a partir dos votos aplicando a regra de empate configurada.
    /// Assume que RevokeMvp() já foi chamado antes desta operação.
    /// </summary>
    private void ApplyMvpTieRule(Domain.Enums.MvpTieRule tieRule, int tieMaxPlayers)
    {
        var voteGroups = Votes
            .Where(v => v.VotedForId != Guid.Empty)
            .GroupBy(v => v.VotedForId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToList();

        if (voteGroups.Count == 0) return;

        var maxCount  = voteGroups.Max(x => x.Count);
        var tied      = voteGroups.Where(x => x.Count == maxCount).ToList();
        var tiedCount = tied.Count;

        // Sem empate — um único líder sempre recebe MVP independente da regra
        if (tiedCount == 1)
        {
            Players.FirstOrDefault(p => p.Id == tied[0].Id)?.SetMvp();
            return;
        }

        // Há empate — aplica a regra configurada
        switch (tieRule)
        {
            case Domain.Enums.MvpTieRule.NoMvp:
                // Ninguém recebe MVP
                break;

            case Domain.Enums.MvpTieRule.AllMvp:
                // Todos os empatados recebem MVP
                var allIds = tied.Select(t => t.Id).ToHashSet();
                foreach (var p in Players.Where(p => allIds.Contains(p.Id)))
                    p.SetMvp();
                break;

            case Domain.Enums.MvpTieRule.AllMvpUpToMax:
                // Todos recebem MVP apenas se o número de empatados <= máximo configurado
                if (tiedCount <= tieMaxPlayers)
                {
                    var upToMaxIds = tied.Select(t => t.Id).ToHashSet();
                    foreach (var p in Players.Where(p => upToMaxIds.Contains(p.Id)))
                        p.SetMvp();
                }
                break;
        }
    }

    public void AddPlayer(MatchPlayerEntity matchPlayer, PlayerEntity playerEntity)
    {
        ArgumentNullException.ThrowIfNull(matchPlayer);
        ArgumentNullException.ThrowIfNull(playerEntity);

        if (Status != MatchStatus.Created && Status != MatchStatus.Acceptation)
            throw new InvalidOperationException("So e possivel sincronizar jogadores quando a partida esta Created ou Acceptation.");

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
        EnsureStatus(MatchStatus.MatchMaking, "So e possivel atribuir times quando a partida esta em MatchMaking.");

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

    public void SetPlayerRole(Guid matchPlayerId, bool isGoalkeeper)
    {
        if (Status != MatchStatus.Acceptation && Status != MatchStatus.MatchMaking)
            throw new InvalidOperationException("Função só pode ser alterada durante Acceptation ou MatchMaking.");
        var mp = Players.FirstOrDefault(p => p.Id == matchPlayerId)
            ?? throw new InvalidOperationException("Jogador não encontrado na partida.");
        mp.SetIsGoalkeeper(isGoalkeeper);
    }

    public void SwapPlayers(Guid matchPlayerAId, Guid matchPlayerBId)
    {
        EnsureStatus(MatchStatus.MatchMaking, "So e possivel trocar jogadores quando a partida esta em MatchMaking.");

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

    public void AddGoalByMatchPlayer(Guid scorerMatchPlayerId, Guid? assistMatchPlayerId, int? timeSeconds, bool isOwnGoal = false)
    {
        if (Status != MatchStatus.Started && Status != MatchStatus.PostGame && Status != MatchStatus.Finalized)
            throw new InvalidOperationException("So e possivel registrar gols quando a partida esta Started, PostGame ou Finalized.");

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

            // Em gols contra não é exigido que a assistência seja do mesmo time
            if (!isOwnGoal && assistMp.Team != scorerMp.Team)
                throw new InvalidOperationException("A assistencia so pode ser de um jogador do mesmo time do autor do gol.");
        }

        var goal = new GoalEntity(
            matchId: Id,
            groupId: GroupId,
            scorerMatchPlayerId: scorerMp.Id,
            assistMatchPlayerId: assistMp?.Id,
            timeSeconds: timeSeconds,
            isOwnGoal: isOwnGoal);

        Goals.Add(goal);

        RecalculateScoreFromGoals();
    }

    public void UpdateGoal(Guid goalId, Guid scorerMatchPlayerId, Guid? assistMatchPlayerId, int? timeSeconds, bool isOwnGoal = false)
    {
        var goal = Goals.FirstOrDefault(g => g.Id == goalId)
            ?? throw new InvalidOperationException("Gol nao encontrado.");

        if (scorerMatchPlayerId == Guid.Empty)
            throw new InvalidOperationException("MatchPlayerId do gol e obrigatorio.");

        var scorerMp = Players.FirstOrDefault(p => p.Id == scorerMatchPlayerId)
            ?? throw new InvalidOperationException("O jogador do gol nao pertence a esta partida.");

        if (scorerMp.Team == 0)
            throw new InvalidOperationException("Nao e possivel registrar gol de jogador nao atribuido a um time.");

        MatchPlayerEntity? assistMp = null;
        if (assistMatchPlayerId.HasValue)
        {
            assistMp = Players.FirstOrDefault(p => p.Id == assistMatchPlayerId.Value)
                ?? throw new InvalidOperationException("O jogador da assistencia nao pertence a esta partida.");

            if (assistMp.Team == 0)
                throw new InvalidOperationException("Nao e possivel registrar assistencia de jogador nao atribuido a um time.");

            if (!isOwnGoal && assistMp.Team != scorerMp.Team)
                throw new InvalidOperationException("A assistencia so pode ser de um jogador do mesmo time do autor do gol.");
        }

        goal.Update(scorerMp.Id, assistMp?.Id, timeSeconds, isOwnGoal);

        RecalculateScoreFromGoals();
    }

    public void RemoveGoal(Guid goalId)
    {

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

            if (g.IsOwnGoal)
            {
                // Gol contra: o ponto vai para o time adversário
                if (team == 1) countB++;
                else if (team == 2) countA++;
            }
            else
            {
                if (team == 1) countA++;
                else if (team == 2) countB++;
            }
        }

        TeamAGoals = countA;
        TeamBGoals = countB;
    }

    public void RewindOneStep()
    {
        if (Status == MatchStatus.Finalized)
            throw new InvalidOperationException("Partida finalizada. Nao e possivel voltar status.");

        switch (Status)
        {
            case MatchStatus.Created:
                throw new InvalidOperationException("Nao e possivel voltar status quando a partida esta Created.");

            case MatchStatus.Acceptation:
                Status = MatchStatus.Created;
                break;
            case MatchStatus.MatchMaking:
                Status = MatchStatus.Acceptation;
                break;
            case MatchStatus.Started:
                Status = MatchStatus.MatchMaking;
                break;
            case MatchStatus.Ended:
                Status = MatchStatus.Started;
                break;
            case MatchStatus.PostGame:
                Status = MatchStatus.Ended;
                break;
            default:
                throw new InvalidOperationException("Status invalido para rewind.");
        }
    }
}