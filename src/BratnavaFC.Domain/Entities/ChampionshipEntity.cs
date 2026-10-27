using System.Text.Json;
using BratnavaFC.Domain.Championships;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Campeonato do dia: agrupa vários jogos (MatchEntity com ChampionshipId).
/// Os jogos ficam fora das consultas comuns de partidas por filtro global,
/// para não contarem nas estatísticas sem respeitar as regras do campeonato.
/// </summary>
public class ChampionshipEntity : BaseEntity
{
    private static readonly JsonSerializerOptions RulesJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Regras usadas quando o admin não escolhe nada: "fica quem ganha", conta como 1 partida.</summary>
    public static ChampionshipRuleSet DefaultRules { get; } = new(
        HistoryMode: HistoryMode.Consolidated,
        ResultMode: ResultMode.Placement,
        PlacementResults: [PlacementResult.Win, PlacementResult.Draw, PlacementResult.Loss],
        MvpMode: MvpMode.PerChampionship,
        MvpCountsInGeneralStats: true,
        GoalsCountInGeneralStats: false,
        IncludeInTeamDraw: false,
        MinGamesForPlacementResult: 1,
        Format: ChampionshipFormat.WinnerStays,
        RoundRobinTurns: 1,
        WinnerStays: new WinnerStaysRule(0, WinnerStaysTieRule.LongestOnFieldLeaves),
        GameEnd: new GameEndRule(10, 2),
        ChampionshipEnd: new ChampionshipEndRule(ChampionshipEndMode.TotalTime, 120, null),
        Standings: new StandingsRule(StandingMode.PointsPerGame, 3, 1,
            [TiebreakerCriteria.Points, TiebreakerCriteria.GoalDifference, TiebreakerCriteria.GoalsFor,
             TiebreakerCriteria.HeadToHead, TiebreakerCriteria.ExternalDecision]));

    private ChampionshipRuleSet? _rules;

    private ChampionshipEntity() { } // EF

    public ChampionshipEntity(Guid groupId, string name, DateTime playedAt, string placeName)
        : this(groupId, name, playedAt, placeName, DefaultRules) { }

    public ChampionshipEntity(Guid groupId, string name, DateTime playedAt, string placeName, ChampionshipRuleSet rules)
    {
        if (groupId == Guid.Empty) throw new InvalidOperationException("GroupId e obrigatorio.");

        GroupId = groupId;
        SetDetails(name, playedAt, placeName);
        SetRules(rules);
        Status = ChampionshipStatus.Created;
    }

    public Guid GroupId { get; private set; }
    public GroupEntity? Group { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public DateTime PlayedAt { get; private set; }
    public string PlaceName { get; private set; } = string.Empty;

    public ChampionshipStatus Status { get; private set; }

    /// <summary>Quando o primeiro jogo pôde começar (status InProgress).</summary>
    public DateTime? StartedAt { get; private set; }

    /// <summary>Regras serializadas (coluna). Use <see cref="Rules"/>.</summary>
    public string RulesJson { get; private set; } = "{}";

    public ChampionshipRuleSet Rules =>
        _rules ??= JsonSerializer.Deserialize<ChampionshipRuleSet>(RulesJson, RulesJsonOptions)
                   ?? throw new InvalidOperationException("Regras do campeonato invalidas.");

    public List<MatchEntity> Matches { get; private set; } = [];
    public List<ChampionshipTeamEntity> Teams { get; private set; } = [];
    public List<ChampionshipPlayerEntity> Players { get; private set; } = [];
    public List<ChampionshipVoteEntity> Votes { get; private set; } = [];

    /// <summary>Regras e dados só mudam até o primeiro jogo poder começar.</summary>
    public bool RulesLocked => Status >= ChampionshipStatus.InProgress;

    // ── Criação e regras ─────────────────────────────────────────────────────

    public void UpdateDetails(string name, DateTime playedAt, string placeName, ChampionshipRuleSet rules)
    {
        if (RulesLocked)
            throw new InvalidOperationException("As regras nao podem mudar depois que o campeonato comecou.");

        SetDetails(name, playedAt, placeName);
        SetRules(rules);
    }

    private void SetDetails(string name, DateTime playedAt, string placeName)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Nome do campeonato e obrigatorio.");
        if (string.IsNullOrWhiteSpace(placeName)) throw new InvalidOperationException("Local do campeonato e obrigatorio.");

        Name = name.Trim();
        PlayedAt = playedAt;
        PlaceName = placeName.Trim();
    }

    private void SetRules(ChampionshipRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();

        RulesJson = JsonSerializer.Serialize(rules, RulesJsonOptions);
        _rules = rules;
    }

    // ── Presença ─────────────────────────────────────────────────────────────

    /// <summary>Abre a confirmação de presença convidando todos os jogadores ativos (não convidados).</summary>
    public void OpenAcceptation(IEnumerable<PlayerEntity> groupPlayers)
    {
        EnsureStatus(ChampionshipStatus.Created, "So e possivel abrir a presenca quando o campeonato esta Created.");

        foreach (var player in groupPlayers.Where(p => !p.IsGuest))
            AddParticipant(player);

        Status = ChampionshipStatus.Acceptation;
    }

    /// <summary>Adiciona um jogador (ex.: convidado) já com presença confirmada.</summary>
    public void AddConfirmedPlayer(PlayerEntity player)
    {
        EnsureStatusIn("So e possivel adicionar jogadores durante a presenca, o sorteio ou os jogos.",
            ChampionshipStatus.Acceptation, ChampionshipStatus.TeamDraw, ChampionshipStatus.InProgress);

        var participant = Players.FirstOrDefault(p => p.PlayerId == player.Id) ?? AddParticipant(player);
        participant.Accept();
    }

    public void AcceptInvite(Guid playerId)
    {
        EnsureStatusIn("So e possivel confirmar presenca durante a presenca, o sorteio ou os jogos.",
            ChampionshipStatus.Acceptation, ChampionshipStatus.TeamDraw, ChampionshipStatus.InProgress);
        FindParticipant(playerId).Accept();
    }

    public void RejectInvite(Guid playerId)
    {
        EnsureStatusIn("So e possivel recusar presenca antes do campeonato comecar.",
            ChampionshipStatus.Acceptation, ChampionshipStatus.TeamDraw);
        FindParticipant(playerId).Reject();
    }

    private ChampionshipPlayerEntity AddParticipant(PlayerEntity player)
    {
        if (player.GroupId != GroupId)
            throw new InvalidOperationException("Jogador nao pertence a patota do campeonato.");

        var existing = Players.FirstOrDefault(p => p.PlayerId == player.Id);
        if (existing is not null) return existing;

        var participant = new ChampionshipPlayerEntity(Id, player);
        Players.Add(participant);
        return participant;
    }

    // ── Times ────────────────────────────────────────────────────────────────

    public sealed record TeamSpec(string Name, Guid? ColorId, IReadOnlyList<Guid> PlayerIds, Guid? GoalkeeperPlayerId);

    /// <summary>Define (ou redefine) os times. Só antes do primeiro jogo.</summary>
    public void DefineTeams(IReadOnlyList<TeamSpec> teams)
    {
        EnsureStatusIn("So e possivel definir os times durante a presenca ou o sorteio.",
            ChampionshipStatus.Acceptation, ChampionshipStatus.TeamDraw);
        ArgumentNullException.ThrowIfNull(teams);

        if (teams.Count < Rules.MinimumTeams)
            throw new InvalidOperationException($"Esse formato precisa de ao menos {Rules.MinimumTeams} times.");

        var allIds = teams.SelectMany(t => t.PlayerIds ?? []).ToList();
        if (allIds.Count != allIds.Distinct().Count())
            throw new InvalidOperationException("Um jogador nao pode estar em dois times.");

        var colors = teams.Where(t => t.ColorId.HasValue).Select(t => t.ColorId!.Value).ToList();
        if (colors.Count != colors.Distinct().Count())
            throw new InvalidOperationException("Dois times nao podem ter a mesma cor.");

        foreach (var spec in teams)
        {
            if (spec.PlayerIds is null || spec.PlayerIds.Count == 0)
                throw new InvalidOperationException($"O time {spec.Name} precisa de ao menos 1 jogador.");
            if (spec.GoalkeeperPlayerId.HasValue && !spec.PlayerIds.Contains(spec.GoalkeeperPlayerId.Value))
                throw new InvalidOperationException($"O goleiro do time {spec.Name} precisa estar no time.");
            foreach (var playerId in spec.PlayerIds)
            {
                if (FindParticipant(playerId).InviteResponse != InviteResponse.Accepted)
                    throw new InvalidOperationException("So jogadores com presenca confirmada podem entrar em um time.");
            }
        }

        Teams.Clear();
        foreach (var p in Players)
            p.AssignTeam(null, p.IsGoalkeeper);

        for (var i = 0; i < teams.Count; i++)
        {
            var spec = teams[i];
            var team = new ChampionshipTeamEntity(Id, spec.Name, spec.ColorId, i + 1);
            Teams.Add(team);
            foreach (var playerId in spec.PlayerIds)
                FindParticipant(playerId).AssignTeam(team.Id, playerId == spec.GoalkeeperPlayerId);
        }

        Status = ChampionshipStatus.TeamDraw;
    }

    /// <summary>Troca o time de um jogador (ex.: chegou atrasado). Não altera jogos já feitos.</summary>
    public void MovePlayer(Guid playerId, Guid? teamId)
    {
        EnsureStatusIn("So e possivel trocar jogadores de time durante o sorteio ou os jogos.",
            ChampionshipStatus.TeamDraw, ChampionshipStatus.InProgress);

        var participant = FindParticipant(playerId);
        if (participant.InviteResponse != InviteResponse.Accepted)
            throw new InvalidOperationException("So jogadores com presenca confirmada podem entrar em um time.");
        if (teamId.HasValue && Teams.All(t => t.Id != teamId.Value))
            throw new InvalidOperationException("Time nao encontrado neste campeonato.");

        participant.AssignTeam(teamId, teamId.HasValue && participant.IsGoalkeeper);
    }

    /// <summary>Define o goleiro do time para os próximos jogos (o jogo em andamento não muda).</summary>
    public void SetTeamGoalkeeper(Guid teamId, Guid playerId)
    {
        EnsureStatusIn("So e possivel trocar o goleiro durante o sorteio ou os jogos.",
            ChampionshipStatus.TeamDraw, ChampionshipStatus.InProgress);

        var participant = FindParticipant(playerId);
        if (participant.TeamId != teamId)
            throw new InvalidOperationException("O goleiro precisa estar no time.");

        foreach (var teammate in Players.Where(p => p.TeamId == teamId))
            teammate.AssignTeam(teamId, teammate.PlayerId == playerId);
    }

    // ── Jogos ────────────────────────────────────────────────────────────────

    public void Start(DateTime nowUtc)
    {
        EnsureStatus(ChampionshipStatus.TeamDraw, "So e possivel iniciar o campeonato depois de definir os times.");
        if (Teams.Count < Rules.MinimumTeams)
            throw new InvalidOperationException($"Esse formato precisa de ao menos {Rules.MinimumTeams} times.");

        StartedAt = nowUtc;
        Status = ChampionshipStatus.InProgress;
    }

    /// <summary>
    /// Cria e já inicia o próximo jogo entre dois times do campeonato. Os jogadores entram
    /// pela escalação atual de cada time; quem não apareceu (DidNotPlay) fica de fora.
    /// </summary>
    public MatchEntity CreateGame(
        Guid teamAId, Guid teamBId, int order,
        IReadOnlyDictionary<Guid, PlayerEntity> playersById, DateTime nowUtc)
    {
        EnsureStatus(ChampionshipStatus.InProgress, "So e possivel criar jogos com o campeonato em andamento.");
        if (teamAId == teamBId) throw new InvalidOperationException("Um time nao pode jogar contra ele mesmo.");

        var teamA = Teams.FirstOrDefault(t => t.Id == teamAId) ?? throw new InvalidOperationException("Time A nao encontrado.");
        var teamB = Teams.FirstOrDefault(t => t.Id == teamBId) ?? throw new InvalidOperationException("Time B nao encontrado.");

        var lineupA = Lineup(teamA.Id);
        var lineupB = Lineup(teamB.Id);
        if (lineupA.Count == 0 || lineupB.Count == 0)
            throw new InvalidOperationException("Os dois times precisam de ao menos 1 jogador.");

        var game = new MatchEntity(GroupId, nowUtc, PlaceName);
        game.AssignToChampionship(Id);
        game.SetChampionshipGame(teamA.Id, teamB.Id, order);

        foreach (var participant in lineupA.Concat(lineupB))
        {
            var player = playersById.TryGetValue(participant.PlayerId, out var p)
                ? p
                : throw new InvalidOperationException("Jogador do time nao encontrado.");
            game.AddPlayer(new MatchPlayerEntity(player.Id), player);
        }

        game.OpenAcceptation();
        foreach (var participant in lineupA.Concat(lineupB))
            game.AcceptInvite(participant.PlayerId);

        game.GoToMatchMaking();
        game.AssignTeams(lineupA.Select(p => p.PlayerId).ToArray(), lineupB.Select(p => p.PlayerId).ToArray());

        foreach (var participant in lineupA.Concat(lineupB))
        {
            var matchPlayer = game.Players.First(mp => mp.PlayerId == participant.PlayerId);
            game.SetPlayerRole(matchPlayer.Id, participant.IsGoalkeeper);
        }

        if (teamA.ColorId.HasValue && teamB.ColorId.HasValue && teamA.ColorId != teamB.ColorId)
            game.SetTeamColors(teamA.ColorId, teamB.ColorId);

        game.Start();
        return game;
    }

    /// <summary>Encerra o jogo e grava o placar como definitivo (MVP é decidido no fim do campeonato).</summary>
    public static void EndGame(MatchEntity game, Guid? externalWinnerTeamId)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Status != MatchStatus.Started)
            throw new InvalidOperationException("Esse jogo nao esta em andamento.");

        game.End();
        game.GoToPostGame();
        if (!game.TeamAGoals.HasValue || !game.TeamBGoals.HasValue)
            game.SetScore(game.TeamAGoals ?? 0, game.TeamBGoals ?? 0);

        game.SetChampionshipExternalWinner(externalWinnerTeamId);
        game.FinalizeByVotes();
    }

    private List<ChampionshipPlayerEntity> Lineup(Guid teamId) => Players
        .Where(p => p.TeamId == teamId && p.InviteResponse == InviteResponse.Accepted && !p.DidNotPlay)
        .ToList();

    // ── Encerramento ─────────────────────────────────────────────────────────

    public void Finish()
    {
        EnsureStatus(ChampionshipStatus.InProgress, "So e possivel encerrar os jogos com o campeonato em andamento.");
        Status = ChampionshipStatus.PostGame;
    }

    public void Vote(Guid voterPlayerId, Guid votedPlayerId, Guid? gameId)
    {
        EnsureStatus(ChampionshipStatus.PostGame, "So e possivel votar no MVP depois dos jogos.");

        switch (Rules.MvpMode)
        {
            case MvpMode.None:
                throw new InvalidOperationException("Este campeonato nao tem MVP.");
            case MvpMode.PerChampionship when gameId.HasValue:
                throw new InvalidOperationException("Neste campeonato o MVP e do campeonato, nao de cada jogo.");
            case MvpMode.PerGame when !gameId.HasValue:
                throw new InvalidOperationException("Informe o jogo do voto.");
        }

        if (voterPlayerId == votedPlayerId)
            throw new InvalidOperationException("O jogador nao pode votar em si mesmo.");

        var voter = FindParticipant(voterPlayerId);
        if (voter.Player?.IsGuest == true)
            throw new InvalidOperationException("Convidados não podem votar no MVP.");
        if (voter.TeamId is null || voter.DidNotPlay)
            throw new InvalidOperationException("Apenas quem jogou o campeonato pode votar.");

        var voted = FindParticipant(votedPlayerId);
        if (voted.TeamId is null || voted.DidNotPlay)
            throw new InvalidOperationException("Apenas quem jogou pode ser votado.");

        if (Votes.Any(v => v.VoterPlayerId == voterPlayerId && v.GameId == gameId))
            throw new InvalidOperationException("Esse jogador ja votou.");

        Votes.Add(new ChampionshipVoteEntity(Id, gameId, voterPlayerId, votedPlayerId));
    }

    /// <summary>
    /// Finaliza: grava a posição de cada time (empatados dividem a posição) e aplica o MVP
    /// com a regra de empate da patota.
    /// <paramref name="games"/> são os jogos do campeonato (necessários no MVP por jogo).
    /// </summary>
    public void Finalize(
        IReadOnlyDictionary<Guid, int> finalPositions,
        IReadOnlyList<MatchEntity> games,
        MvpTieRule tieRule,
        int tieMaxPlayers)
    {
        EnsureStatus(ChampionshipStatus.PostGame, "So e possivel finalizar depois de encerrar os jogos.");

        foreach (var team in Teams)
            team.SetFinalPosition(finalPositions.TryGetValue(team.Id, out var position) ? position : null);

        foreach (var participant in Players)
            participant.SetMvp(false);

        if (Rules.MvpMode == MvpMode.PerChampionship)
        {
            foreach (var playerId in ResolveMvps(Votes.Where(v => v.GameId is null), tieRule, tieMaxPlayers))
                FindParticipant(playerId).SetMvp(true);
        }
        else if (Rules.MvpMode == MvpMode.PerGame)
        {
            foreach (var game in games)
            {
                foreach (var mp in game.Players) mp.RevokeMvp();
                foreach (var playerId in ResolveMvps(Votes.Where(v => v.GameId == game.Id), tieRule, tieMaxPlayers))
                    game.Players.FirstOrDefault(mp => mp.PlayerId == playerId)?.SetMvp();
            }
        }

        Status = ChampionshipStatus.Finalized;
    }

    /// <summary>Mesma regra de empate das partidas (MatchEntity.ApplyMvpTieRule).</summary>
    private static IReadOnlyList<Guid> ResolveMvps(IEnumerable<ChampionshipVoteEntity> votes, MvpTieRule tieRule, int tieMaxPlayers)
    {
        var counts = votes.GroupBy(v => v.VotedPlayerId).Select(g => (Id: g.Key, Count: g.Count())).ToList();
        if (counts.Count == 0) return [];

        var max = counts.Max(c => c.Count);
        var tied = counts.Where(c => c.Count == max).Select(c => c.Id).ToList();
        if (tied.Count == 1) return tied;

        return tieRule switch
        {
            MvpTieRule.NoMvp => [],
            MvpTieRule.AllMvpUpToMax when tied.Count > tieMaxPlayers => [],
            _ => tied
        };
    }

    public void Cancel()
    {
        if (Status is ChampionshipStatus.Finalized or ChampionshipStatus.Cancelled)
            throw new InvalidOperationException("Esse campeonato ja foi encerrado.");
        Status = ChampionshipStatus.Cancelled;
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    public ChampionshipPlayerEntity FindParticipant(Guid playerId) =>
        Players.FirstOrDefault(p => p.PlayerId == playerId)
        ?? throw new InvalidOperationException("Jogador nao encontrado neste campeonato.");

    private void EnsureStatus(ChampionshipStatus required, string message)
    {
        if (Status != required) throw new InvalidOperationException(message);
    }

    private void EnsureStatusIn(string message, params ChampionshipStatus[] allowed)
    {
        if (!allowed.Contains(Status)) throw new InvalidOperationException(message);
    }
}
