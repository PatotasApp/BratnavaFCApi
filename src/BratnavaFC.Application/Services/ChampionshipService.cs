using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Championships;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Championships;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Modo Campeonato (Fases 1 e 2): criação, presença, times, jogos, classificação e MVP.
///
/// Os jogos são MatchEntity com ChampionshipId e ficam escondidos pelo filtro global
/// (Fase 0). Este serviço é o único lugar que os enxerga, sempre por
/// <see cref="GamesQuery"/>, e está na allowlist do teste de arquitetura.
/// Estatísticas gerais respeitando as regras do campeonato ficam para a Fase 3.
/// </summary>
public sealed class ChampionshipService : IChampionshipService
{
    private readonly AppDbContext _db;

    public ChampionshipService(AppDbContext db) => _db = db;

    // ── Consulta ─────────────────────────────────────────────────────────────

    public async Task<Result<List<ChampionshipSummaryDto>>> ListAsync(Guid groupId, CancellationToken ct)
    {
        var championships = await _db.Championships.AsNoTracking()
            .Where(c => c.GroupId == groupId)
            .Include(c => c.Teams).ThenInclude(t => t.Color)
            .OrderByDescending(c => c.PlayedAt)
            .AsSplitQuery()
            .ToListAsync(ct);

        var ids = championships.Select(c => c.Id).ToList();
        var gameCounts = await GamesQuery()
            .Where(m => ids.Contains(m.ChampionshipId!.Value))
            .GroupBy(m => m.ChampionshipId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return Result<List<ChampionshipSummaryDto>>.Ok(championships.Select(c =>
        {
            var champion = c.Teams.FirstOrDefault(t => t.FinalPosition == 1);
            return new ChampionshipSummaryDto(
                c.Id, c.Name, c.PlayedAt, c.PlaceName, c.Status, c.Teams.Count,
                gameCounts.GetValueOrDefault(c.Id),
                champion?.Name, champion?.Color?.HexValue, c.Rules.BuildBadges());
        }).ToList());
    }

    public async Task<Result<ChampionshipDetailsDto>> GetAsync(Guid groupId, Guid championshipId, Guid? userId, CancellationToken ct)
    {
        var championship = await LoadAsync(groupId, championshipId, tracking: false, ct);
        if (championship is null) return NotFound<ChampionshipDetailsDto>();

        var games = await LoadGamesAsync(championshipId, tracking: false, ct);
        var colors = await _db.TeamColors.AsNoTracking()
            .Where(c => c.GroupId == groupId)
            .ToDictionaryAsync(c => c.Id, c => c.HexValue, ct);

        var history = FinishedHistory(games);
        var teamIds = OrderedTeamIds(championship);
        var standings = teamIds.Count >= 2
            ? StandingsCalculator.Calculate(teamIds, history, championship.Rules.ToStandingsRules())
            : [];

        IReadOnlyList<Guid> waiting = [];
        if (championship.Status == ChampionshipStatus.InProgress)
            waiting = SuggestNextGame(championship, games)?.WaitingTeamIds ?? [];

        var playerIdByMatchPlayerId = games.SelectMany(g => g.Players).ToDictionary(mp => mp.Id, mp => mp.PlayerId);
        var goals = games.SelectMany(g => g.Goals).ToList();

        var topScorers = goals.Where(g => !g.IsOwnGoal)
            .GroupBy(g => playerIdByMatchPlayerId[g.ScorerMatchPlayerId])
            .Select(g => new ChampionshipScorerDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Goals).ToList();
        var topAssists = goals.Where(g => !g.IsOwnGoal && g.AssistMatchPlayerId.HasValue)
            .GroupBy(g => playerIdByMatchPlayerId[g.AssistMatchPlayerId!.Value])
            .Select(g => new ChampionshipAssistDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Assists).ToList();

        var mvps = championship.Rules.MvpMode == MvpMode.PerGame
            ? games.SelectMany(g => g.Players).Where(mp => mp.IsMvp == true).Select(mp => mp.PlayerId).Distinct().ToList()
            : championship.Players.Where(p => p.IsMvp).Select(p => p.PlayerId).ToList();

        var me = userId.HasValue
            ? championship.Players.FirstOrDefault(p => p.Player?.UserId == userId.Value)
            : null;
        var myVotes = me is null
            ? []
            : championship.Votes.Where(v => v.VoterPlayerId == me.PlayerId)
                .Select(v => new ChampionshipMyVoteDto(v.GameId, v.VotedPlayerId)).ToList();

        var dto = new ChampionshipDetailsDto(
            championship.Id, championship.Name, championship.PlayedAt, championship.PlaceName, championship.Status,
            championship.Rules, championship.RulesLocked, championship.Rules.BuildBadges(),
            championship.Players
                .OrderBy(p => p.Player?.Name)
                .Select(p => new ChampionshipParticipantDto(
                    p.PlayerId, p.Player?.Name ?? "", null, p.Player?.IsGuest ?? false,
                    p.IsGoalkeeper, p.InviteResponse, p.TeamId, p.DidNotPlay))
                .ToList(),
            championship.Teams.OrderBy(t => t.Order)
                .Select(t => new ChampionshipTeamDto(
                    t.Id, t.Name, t.ColorId,
                    t.ColorId.HasValue ? colors.GetValueOrDefault(t.ColorId.Value) : null,
                    t.FinalPosition,
                    championship.Players.Where(p => p.TeamId == t.Id).Select(p => p.PlayerId).ToList()))
                .ToList(),
            games.Select(g => new ChampionshipGameDto(
                    g.Id, g.ChampionshipGameOrder ?? 0, g.ChampionshipTeamAId!.Value, g.ChampionshipTeamBId!.Value,
                    g.TeamAGoals ?? 0, g.TeamBGoals ?? 0, g.ChampionshipExternalWinnerTeamId,
                    g.Status == MatchStatus.Finalized ? ChampionshipGameStatus.Finished : ChampionshipGameStatus.InProgress,
                    g.ActualStartTime,
                    g.Goals.Select(goal => new ChampionshipGameGoalDto(
                        goal.Id, playerIdByMatchPlayerId[goal.ScorerMatchPlayerId],
                        goal.AssistMatchPlayerId.HasValue ? playerIdByMatchPlayerId[goal.AssistMatchPlayerId.Value] : null,
                        goal.IsOwnGoal, goal.TimeSeconds)).ToList()))
                .ToList(),
            standings.Select(s => new ChampionshipStandingDto(
                s.Position, s.TeamId, s.Played, s.Wins, s.Draws, s.Losses, s.GoalsFor, s.GoalsAgainst,
                s.GoalDifference, s.Points, s.PointsPerGame)).ToList(),
            games.FirstOrDefault(g => g.Status == MatchStatus.Started)?.Id,
            waiting,
            topScorers,
            topAssists,
            mvps,
            me?.InviteResponse,
            CanVote(championship, me, games),
            myVotes);

        return Result<ChampionshipDetailsDto>.Ok(dto);
    }

    public async Task<Result<NextGameSuggestionDto?>> GetNextGameAsync(Guid groupId, Guid championshipId, CancellationToken ct)
    {
        var championship = await LoadAsync(groupId, championshipId, tracking: false, ct);
        if (championship is null) return NotFound<NextGameSuggestionDto?>();
        if (championship.Status != ChampionshipStatus.InProgress)
            return Result<NextGameSuggestionDto?>.Fail("O campeonato nao esta em andamento.");

        var games = await LoadGamesAsync(championshipId, tracking: false, ct);
        return Result<NextGameSuggestionDto?>.Ok(SuggestNextGame(championship, games));
    }

    // ── Criação e regras ─────────────────────────────────────────────────────

    public Task<Result<ChampionshipCreatedDto>> CreateAsync(Guid groupId, CreateChampionshipDto dto, CancellationToken ct) =>
        Run(async () =>
        {
            if (!await _db.Groups.AnyAsync(g => g.Id == groupId, ct))
                return Result<ChampionshipCreatedDto>.Fail("Patota nao encontrada.", ResultStatus.NotFound);

            var championship = new ChampionshipEntity(
                groupId, dto.Name, dto.PlayedAt, dto.PlaceName, dto.Rules ?? ChampionshipEntity.DefaultRules);
            _db.Championships.Add(championship);
            await _db.SaveChangesAsync(ct);
            return Result<ChampionshipCreatedDto>.Ok(new ChampionshipCreatedDto(championship.Id), status: ResultStatus.Created);
        });

    public Task<Result> UpdateAsync(Guid groupId, Guid championshipId, UpdateChampionshipDto dto, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, c =>
        {
            c.UpdateDetails(dto.Name, dto.PlayedAt, dto.PlaceName, dto.Rules);
            return Task.CompletedTask;
        });

    public Task<Result> CancelAsync(Guid groupId, Guid championshipId, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, async c =>
        {
            var games = await LoadGamesAsync(championshipId, tracking: true, ct);
            if (games.Any(g => g.Status == MatchStatus.Finalized))
                throw new InvalidOperationException("O campeonato ja tem jogos encerrados e nao pode ser cancelado.");

            _db.Matches.RemoveRange(games);
            c.Cancel();
        });

    // ── Presença ─────────────────────────────────────────────────────────────

    public Task<Result> OpenAcceptationAsync(Guid groupId, Guid championshipId, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, async c =>
        {
            var players = await _db.Players
                .Where(p => p.GroupId == groupId && p.Status == Status.Active && !p.IsGuest)
                .ToListAsync(ct);
            c.OpenAcceptation(players);
        });

    public Task<Result> RespondMyInviteAsync(Guid groupId, Guid championshipId, Guid userId, bool accept, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, c =>
        {
            var me = c.Players.FirstOrDefault(p => p.Player?.UserId == userId)
                ?? throw new InvalidOperationException("Voce nao foi convidado para este campeonato.");
            if (accept) c.AcceptInvite(me.PlayerId); else c.RejectInvite(me.PlayerId);
            return Task.CompletedTask;
        });

    public Task<Result> RespondInviteAsync(Guid groupId, Guid championshipId, Guid playerId, bool accept, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, c =>
        {
            if (accept) c.AcceptInvite(playerId); else c.RejectInvite(playerId);
            return Task.CompletedTask;
        });

    public Task<Result> AddGuestAsync(Guid groupId, Guid championshipId, Guid playerId, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, async c =>
        {
            var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId && p.GroupId == groupId, ct)
                ?? throw new InvalidOperationException("Jogador nao encontrado na patota.");
            c.AddConfirmedPlayer(player);
        });

    // ── Times ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sugestão provisória de times (não salva): goleiros primeiro, um por time, e os demais
    /// em "cobra" pela nota. Será trocada pelo algoritmo novo de sorteio para N times.
    /// </summary>
    public async Task<Result<DefineTeamsDto>> DrawTeamsAsync(Guid groupId, Guid championshipId, int teamCount, CancellationToken ct)
    {
        var championship = await LoadAsync(groupId, championshipId, tracking: false, ct);
        if (championship is null) return NotFound<DefineTeamsDto>();
        if (teamCount < championship.Rules.MinimumTeams)
            return Result<DefineTeamsDto>.Fail($"Esse formato precisa de ao menos {championship.Rules.MinimumTeams} times.");

        var confirmed = championship.Players
            .Where(p => p.InviteResponse == InviteResponse.Accepted && !p.DidNotPlay && p.Player is not null)
            .ToList();
        if (confirmed.Count < teamCount)
            return Result<DefineTeamsDto>.Fail("Ha menos jogadores confirmados do que times.");

        static double Strength(PlayerEntity p) =>
            p.OverallRating ?? (p.GuestStarRating.HasValue ? p.GuestStarRating.Value * 2 : (double)p.SkillPoints);

        var colors = await _db.TeamColors.AsNoTracking()
            .Where(c => c.GroupId == groupId && c.IsActive)
            .OrderBy(c => c.Name)
            .Take(teamCount)
            .ToListAsync(ct);

        var teams = Enumerable.Range(0, teamCount).Select(i => new DefineTeamDto(
            i < colors.Count ? colors[i].Name : $"Time {i + 1}",
            i < colors.Count ? colors[i].Id : null,
            [],
            null)).ToList();

        var goalkeepers = confirmed.Where(p => p.IsGoalkeeper).OrderByDescending(p => Strength(p.Player!)).ToList();
        var assignedGoalkeepers = goalkeepers.Take(teamCount).ToList();
        for (var i = 0; i < assignedGoalkeepers.Count; i++)
        {
            teams[i].PlayerIds.Add(assignedGoalkeepers[i].PlayerId);
            teams[i] = teams[i] with { GoalkeeperPlayerId = assignedGoalkeepers[i].PlayerId };
        }

        var field = confirmed.Except(assignedGoalkeepers).OrderByDescending(p => Strength(p.Player!)).ToList();
        for (var i = 0; i < field.Count; i++)
        {
            var round = i / teamCount;
            var slot = i % teamCount;
            var teamIndex = round % 2 == 0 ? slot : teamCount - 1 - slot;
            teams[teamIndex].PlayerIds.Add(field[i].PlayerId);
        }

        return Result<DefineTeamsDto>.Ok(new DefineTeamsDto(teams));
    }

    public Task<Result> DefineTeamsAsync(Guid groupId, Guid championshipId, DefineTeamsDto dto, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, async c =>
        {
            var colorIds = dto.Teams.Where(t => t.ColorId.HasValue).Select(t => t.ColorId!.Value).ToList();
            var validColors = await _db.TeamColors.CountAsync(tc => tc.GroupId == groupId && colorIds.Contains(tc.Id), ct);
            if (validColors != colorIds.Distinct().Count())
                throw new InvalidOperationException("Cor de time invalida para esta patota.");

            c.DefineTeams(dto.Teams
                .Select(t => new ChampionshipEntity.TeamSpec(t.Name, t.ColorId, t.PlayerIds ?? [], t.GoalkeeperPlayerId))
                .ToList());
        });

    public Task<Result> MovePlayerAsync(Guid groupId, Guid championshipId, Guid playerId, Guid? teamId, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, c =>
        {
            c.MovePlayer(playerId, teamId);
            return Task.CompletedTask;
        });

    public Task<Result> SetTeamGoalkeeperAsync(Guid groupId, Guid championshipId, SetGameGoalkeeperDto dto, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, c =>
        {
            c.SetTeamGoalkeeper(dto.TeamId, dto.PlayerId);
            return Task.CompletedTask;
        });

    // ── Jogos ────────────────────────────────────────────────────────────────

    public Task<Result> StartAsync(Guid groupId, Guid championshipId, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, c =>
        {
            c.Start(DateTime.UtcNow);
            return Task.CompletedTask;
        });

    public async Task<Result<ChampionshipGameCreatedDto>> CreateGameAsync(
        Guid groupId, Guid championshipId, CreateChampionshipGameDto dto, CancellationToken ct)
    {
        MatchEntity? created = null;
        var result = await Mutate(groupId, championshipId, ct, async c =>
        {
            var games = await LoadGamesAsync(championshipId, tracking: true, ct);
            if (games.Any(g => g.Status == MatchStatus.Started))
                throw new InvalidOperationException("Encerre o jogo em andamento antes de comecar outro.");

            var playerIds = c.Players.Select(p => p.PlayerId).ToList();
            var players = await _db.Players.Where(p => playerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

            var order = games.Count == 0 ? 1 : games.Max(g => g.ChampionshipGameOrder ?? 0) + 1;
            created = c.CreateGame(dto.TeamAId, dto.TeamBId, order, players, DateTime.UtcNow);
            _db.Matches.Add(created);
        });

        return result.Success && created is not null
            ? Result<ChampionshipGameCreatedDto>.Ok(new ChampionshipGameCreatedDto(created.Id), status: ResultStatus.Created)
            : Result<ChampionshipGameCreatedDto>.Fail(result.Error ?? "Falha ao criar o jogo.", result.Status);
    }

    public Task<Result> AddGoalAsync(Guid groupId, Guid championshipId, Guid gameId, ChampionshipGoalDto dto, CancellationToken ct) =>
        MutateGame(groupId, championshipId, gameId, ct, (_, game) =>
        {
            var scorer = game.Players.FirstOrDefault(mp => mp.PlayerId == dto.ScorerPlayerId)
                ?? throw new InvalidOperationException("O autor do gol nao jogou esse jogo.");
            Guid? assistId = null;
            if (dto.AssistPlayerId.HasValue)
            {
                assistId = (game.Players.FirstOrDefault(mp => mp.PlayerId == dto.AssistPlayerId.Value)
                    ?? throw new InvalidOperationException("O autor da assistencia nao jogou esse jogo.")).Id;
            }

            var before = game.Goals.Select(g => g.Id).ToHashSet();
            game.AddGoalByMatchPlayer(scorer.Id, assistId, dto.TimeSeconds, dto.IsOwnGoal);

            // Gol novo com Id gerado no domínio: precisa ser marcado como inserido.
            foreach (var goal in game.Goals.Where(g => !before.Contains(g.Id)))
                _db.Entry(goal).State = EntityState.Added;
        });

    public Task<Result> RemoveGoalAsync(Guid groupId, Guid championshipId, Guid gameId, Guid goalId, CancellationToken ct) =>
        MutateGame(groupId, championshipId, gameId, ct, (_, game) =>
        {
            var goal = game.Goals.FirstOrDefault(g => g.Id == goalId)
                ?? throw new InvalidOperationException("Gol nao encontrado.");
            game.RemoveGoal(goalId);
            _db.Goals.Remove(goal);
        });

    public Task<Result> EndGameAsync(Guid groupId, Guid championshipId, Guid gameId, EndChampionshipGameDto dto, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, async c =>
        {
            var games = await LoadGamesAsync(championshipId, tracking: true, ct);
            var game = games.FirstOrDefault(g => g.Id == gameId)
                ?? throw new InvalidOperationException("Jogo nao encontrado neste campeonato.");

            var tied = (game.TeamAGoals ?? 0) == (game.TeamBGoals ?? 0);
            if (tied && dto.ExternalWinnerTeamId is null && NeedsWinnerOnTie(c, game))
                throw new InvalidOperationException("Jogo empatado: informe quem venceu nos penaltis.");

            ChampionshipEntity.EndGame(game, tied ? dto.ExternalWinnerTeamId : null);
        });

    public Task<Result> FinishAsync(Guid groupId, Guid championshipId, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, async c =>
        {
            var games = await LoadGamesAsync(championshipId, tracking: false, ct);
            if (games.Any(g => g.Status == MatchStatus.Started))
                throw new InvalidOperationException("Encerre o jogo em andamento antes de terminar o campeonato.");
            if (!games.Any(g => g.Status == MatchStatus.Finalized))
                throw new InvalidOperationException("O campeonato precisa de ao menos 1 jogo encerrado.");

            c.Finish();
        });

    // ── Encerramento ─────────────────────────────────────────────────────────

    public Task<Result> VoteAsync(Guid groupId, Guid championshipId, Guid userId, ChampionshipVoteDto dto, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, async c =>
        {
            var voter = c.Players.FirstOrDefault(p => p.Player?.UserId == userId)
                ?? throw new InvalidOperationException("Voce nao participa deste campeonato.");

            if (dto.GameId.HasValue)
            {
                var games = await LoadGamesAsync(championshipId, tracking: false, ct);
                var game = games.FirstOrDefault(g => g.Id == dto.GameId.Value)
                    ?? throw new InvalidOperationException("Jogo nao encontrado neste campeonato.");
                if (game.Players.All(mp => mp.PlayerId != dto.VotedPlayerId))
                    throw new InvalidOperationException("Esse jogador nao jogou esse jogo.");
            }

            c.Vote(voter.PlayerId, dto.VotedPlayerId, dto.GameId);
        });

    public Task<Result> FinalizeAsync(Guid groupId, Guid championshipId, FinalizeChampionshipDto dto, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, async c =>
        {
            var games = await LoadGamesAsync(championshipId, tracking: true, ct);
            var positions = FinalPositions(c, games, dto.ExternalDecisionTeamOrder);

            var settings = await _db.GroupSettings.AsNoTracking().FirstOrDefaultAsync(s => s.GroupId == groupId, ct);
            c.Finalize(positions, games, settings?.MvpTieRule ?? MvpTieRule.AllMvp, settings?.MvpTieMaxPlayers ?? 2);
        });

    // ── Formato: próximo jogo, empates e colocação final ─────────────────────

    private static NextGameSuggestionDto? SuggestNextGame(ChampionshipEntity c, IReadOnlyList<MatchEntity> games)
    {
        var teamIds = OrderedTeamIds(c);
        if (teamIds.Count < 2) return null;

        var history = FinishedHistory(games);
        var rules = c.Rules;
        var names = c.Teams.ToDictionary(t => t.Id, t => t.Name);

        try
        {
            var end = rules.ToChampionshipEndOptions();
            var elapsed = c.StartedAt.HasValue ? DateTime.UtcNow - c.StartedAt.Value : TimeSpan.Zero;
            if (end is not null && history.Count > 0 && ChampionshipEndEvaluator.ShouldEnd(end, teamIds, history, elapsed))
                return null;

            switch (rules.Format)
            {
                case ChampionshipFormat.WinnerStays:
                {
                    var state = WinnerStaysEngine.GetState(teamIds, history, rules.ToWinnerStaysOptions());
                    return new NextGameSuggestionDto(state.NextGame.TeamAId, state.NextGame.TeamBId,
                        $"Fica quem ganha: {names[state.NextGame.TeamAId]} x {names[state.NextGame.TeamBId]}",
                        state.WaitingQueue);
                }
                case ChampionshipFormat.RoundRobin:
                case ChampionshipFormat.RoundRobinWithFinal:
                {
                    var scheduled = RoundRobinScheduleGenerator.Generate(teamIds, rules.RoundRobinTurns)
                        .SelectMany(r => r.Games).ToList();
                    var next = FirstUnplayed(scheduled.Select(g => (g.TeamAId, g.TeamBId)), history);
                    if (next.HasValue)
                        return Suggestion(next.Value, "Proximo jogo da tabela", teamIds);

                    if (rules.Format == ChampionshipFormat.RoundRobinWithFinal && history.Count == scheduled.Count)
                    {
                        var standings = StandingsCalculator.Calculate(teamIds, history, rules.ToStandingsRules());
                        var final = RoundRobinWithFinalEngine.CreateFinal(standings.Select(s => s.TeamId));
                        return Suggestion((final.TeamAId, final.TeamBId), "Final", teamIds);
                    }

                    return null;
                }
                case ChampionshipFormat.Knockout:
                {
                    var round = KnockoutEngine.CreateRound(teamIds);
                    var consumed = 0;
                    while (history.Count - consumed >= round.Games.Count)
                    {
                        if (round.IsFinal) return null;
                        var results = history.Skip(consumed).Take(round.Games.Count).ToList();
                        consumed += round.Games.Count;
                        round = KnockoutEngine.CreateNextRound(round, results);
                    }

                    var game = round.Games[history.Count - consumed];
                    return Suggestion((game.TeamAId, game.TeamBId), round.IsFinal ? "Final" : $"Mata-mata, fase {round.RoundNumber}", teamIds);
                }
                default:
                    return null;
            }
        }
        catch (InvalidOperationException)
        {
            // Jogos criados fora da ordem do formato: o admin escolhe o próximo manualmente.
            return null;
        }

        NextGameSuggestionDto Suggestion((Guid A, Guid B) pair, string reason, IReadOnlyList<Guid> all) =>
            new(pair.A, pair.B, $"{reason}: {names[pair.A]} x {names[pair.B]}",
                all.Where(id => id != pair.A && id != pair.B).ToList());
    }

    private static (Guid, Guid)? FirstUnplayed(IEnumerable<(Guid A, Guid B)> scheduled, IReadOnlyList<ChampionshipMatch> history)
    {
        var played = history.Select(h => PairKey(h.TeamAId, h.TeamBId)).GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        foreach (var game in scheduled)
        {
            var key = PairKey(game.A, game.B);
            if (played.TryGetValue(key, out var count) && count > 0)
            {
                played[key] = count - 1;
                continue;
            }
            return game;
        }
        return null;
    }

    private static (Guid, Guid) PairKey(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);

    private static bool NeedsWinnerOnTie(ChampionshipEntity c, MatchEntity game)
    {
        var rules = c.Rules;
        return rules.Format switch
        {
            ChampionshipFormat.Knockout => true,
            ChampionshipFormat.WinnerStays => rules.WinnerStays?.TieRule == WinnerStaysTieRule.PenaltyShootout,
            ChampionshipFormat.RoundRobinWithFinal =>
                (game.ChampionshipGameOrder ?? 0) > RoundRobinScheduleGenerator
                    .Generate(OrderedTeamIds(c), rules.RoundRobinTurns).Sum(r => r.Games.Count),
            _ => false
        };
    }

    /// <summary>
    /// Posição final de cada time. Mata-mata: campeão, vice e depois quem caiu mais tarde.
    /// Todos contra todos + final: os dois da final e depois a classificação. Demais: classificação.
    /// </summary>
    private static Dictionary<Guid, int> FinalPositions(
        ChampionshipEntity c, IReadOnlyList<MatchEntity> games, IReadOnlyList<Guid>? externalOrder)
    {
        var teamIds = OrderedTeamIds(c);
        var history = FinishedHistory(games);
        var rules = c.Rules;
        var standings = StandingsCalculator.Calculate(teamIds, history, rules.ToStandingsRules(), externalOrder);
        var byStanding = standings.ToDictionary(s => s.TeamId, s => s.Position);

        if (rules.Format == ChampionshipFormat.Knockout)
        {
            // Rodada em que cada time foi eliminado; o campeão "cai" depois de todos.
            var eliminatedAt = teamIds.ToDictionary(id => id, _ => int.MaxValue);
            var round = KnockoutEngine.CreateRound(teamIds);
            var consumed = 0;
            while (history.Count - consumed >= round.Games.Count && round.Games.Count > 0)
            {
                var results = history.Skip(consumed).Take(round.Games.Count).ToList();
                foreach (var result in results)
                    if (result.LoserId is Guid loser) eliminatedAt[loser] = round.RoundNumber;
                consumed += round.Games.Count;
                if (round.IsFinal) break;
                round = KnockoutEngine.CreateNextRound(round, results);
            }

            return RankBy(teamIds, id => eliminatedAt[id]);
        }

        if (rules.Format == ChampionshipFormat.RoundRobinWithFinal)
        {
            var scheduledCount = RoundRobinScheduleGenerator.Generate(teamIds, rules.RoundRobinTurns).Sum(r => r.Games.Count);
            var final = history.Count > scheduledCount ? history[scheduledCount] : null;
            if (final?.WinnerId is Guid winner && final.LoserId is Guid loser)
            {
                var qualifying = StandingsCalculator.Calculate(teamIds, history.Take(scheduledCount), rules.ToStandingsRules(), externalOrder);
                var positions = new Dictionary<Guid, int> { [winner] = 1, [loser] = 2 };
                foreach (var s in qualifying.Where(s => s.TeamId != winner && s.TeamId != loser))
                    positions[s.TeamId] = s.Position;
                return positions;
            }
        }

        return byStanding;
    }

    /// <summary>Maior chave = melhor. Empatados dividem a posição (1, 2, 2, 4...).</summary>
    private static Dictionary<Guid, int> RankBy(IReadOnlyList<Guid> teamIds, Func<Guid, int> key)
    {
        var ordered = teamIds.OrderByDescending(key).ToList();
        var positions = new Dictionary<Guid, int>();
        for (var i = 0; i < ordered.Count; i++)
        {
            positions[ordered[i]] = i > 0 && key(ordered[i]) == key(ordered[i - 1])
                ? positions[ordered[i - 1]]
                : i + 1;
        }
        return positions;
    }

    private static bool CanVote(ChampionshipEntity c, ChampionshipPlayerEntity? me, IReadOnlyList<MatchEntity> games)
    {
        if (c.Status != ChampionshipStatus.PostGame || me is null || c.Rules.MvpMode == MvpMode.None) return false;
        if (me.Player?.IsGuest == true || me.TeamId is null || me.DidNotPlay) return false;

        var myVotes = c.Votes.Where(v => v.VoterPlayerId == me.PlayerId).ToList();
        return c.Rules.MvpMode == MvpMode.PerChampionship
            ? myVotes.Count == 0
            : games.Any(g => g.Status == MatchStatus.Finalized && myVotes.All(v => v.GameId != g.Id));
    }

    // ── Carregamento e execução ──────────────────────────────────────────────

    /// <summary>Única porta para os jogos do campeonato (ignora o filtro global de propósito).</summary>
    private IQueryable<MatchEntity> GamesQuery() => _db.Matches.IgnoreQueryFilters().Where(m => m.ChampionshipId != null);

    private async Task<List<MatchEntity>> LoadGamesAsync(Guid championshipId, bool tracking, CancellationToken ct)
    {
        var query = GamesQuery()
            .Where(m => m.ChampionshipId == championshipId)
            .Include(m => m.Players)
            .Include(m => m.Goals)
            .OrderBy(m => m.ChampionshipGameOrder)
            .AsSplitQuery();
        return await (tracking ? query : query.AsNoTracking()).ToListAsync(ct);
    }

    private Task<ChampionshipEntity?> LoadAsync(Guid groupId, Guid championshipId, bool tracking, CancellationToken ct)
    {
        var query = _db.Championships
            .Where(c => c.GroupId == groupId && c.Id == championshipId)
            .Include(c => c.Teams)
            .Include(c => c.Players).ThenInclude(p => p.Player)
            .Include(c => c.Votes)
            .AsSplitQuery();
        return (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private static List<Guid> OrderedTeamIds(ChampionshipEntity c) => c.Teams.OrderBy(t => t.Order).Select(t => t.Id).ToList();

    private static List<ChampionshipMatch> FinishedHistory(IEnumerable<MatchEntity> games) => games
        .Where(g => g.Status == MatchStatus.Finalized)
        .OrderBy(g => g.ChampionshipGameOrder)
        .Select(g => new ChampionshipMatch(
            g.ChampionshipTeamAId!.Value, g.ChampionshipTeamBId!.Value,
            g.TeamAGoals ?? 0, g.TeamBGoals ?? 0, g.ChampionshipExternalWinnerTeamId))
        .ToList();

    private async Task<Result> Mutate(Guid groupId, Guid championshipId, CancellationToken ct, Func<ChampionshipEntity, Task> action)
    {
        var championship = await LoadAsync(groupId, championshipId, tracking: true, ct);
        if (championship is null) return Result.Fail("Campeonato nao encontrado.", ResultStatus.NotFound);

        try
        {
            await action(championship);
            await _db.SaveChangesAsync(ct);
            return Result.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(ex.Message);
        }
    }

    private Task<Result> MutateGame(Guid groupId, Guid championshipId, Guid gameId, CancellationToken ct,
        Action<ChampionshipEntity, MatchEntity> action) =>
        Mutate(groupId, championshipId, ct, async c =>
        {
            if (c.Status is not (ChampionshipStatus.InProgress or ChampionshipStatus.PostGame))
                throw new InvalidOperationException("Os jogos so podem ser alterados com o campeonato em andamento ou em votacao.");

            var games = await LoadGamesAsync(championshipId, tracking: true, ct);
            var game = games.FirstOrDefault(g => g.Id == gameId)
                ?? throw new InvalidOperationException("Jogo nao encontrado neste campeonato.");
            action(c, game);
        });

    private static async Task<Result<T>> Run<T>(Func<Task<Result<T>>> action)
    {
        try { return await action(); }
        catch (InvalidOperationException ex) { return Result<T>.Fail(ex.Message); }
    }

    private static Result<T> NotFound<T>() => Result<T>.Fail("Campeonato nao encontrado.", ResultStatus.NotFound);
}
