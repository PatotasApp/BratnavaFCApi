using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration.Championship;
using BratnavaFC.Application.TeamGeneration.MultiTeam;
using BratnavaFC.Domain.Championships;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Championships;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Cloudflare;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Redis;
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
    private readonly MultiTeamGenerationService _multiTeamGeneration;
    private readonly IConquistaProjectionService? _projection;
    private readonly IMatchEventPublisher? _eventPublisher;
    private readonly ChampionshipTeamDrawService? _teamDraw;
    private readonly IReplayUrlService? _replayUrls;

    public ChampionshipService(
        AppDbContext db,
        MultiTeamGenerationService multiTeamGeneration,
        IConquistaProjectionService? projection = null,
        IMatchEventPublisher? eventPublisher = null,
        ChampionshipTeamDrawService? teamDraw = null,
        IReplayUrlService? replayUrls = null)
    {
        _teamDraw = teamDraw;
        _replayUrls = replayUrls;
        _db = db;
        _multiTeamGeneration = multiTeamGeneration ?? throw new ArgumentNullException(nameof(multiTeamGeneration));
        _projection = projection;
        _eventPublisher = eventPublisher;
    }

    private ChampionshipBetService Bets => new(_db);

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
            myVotes,
            championship.LinkedPollId);

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
            await Bets.DiscardOpenBetsAsync(championshipId, ct);
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

    public Task<Result> AddGuestAsync(Guid groupId, Guid championshipId, AddChampionshipGuestDto dto, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, async c =>
        {
            PlayerEntity player;
            if (dto.PlayerId is { } playerId)
            {
                player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId && p.GroupId == groupId, ct)
                    ?? throw new InvalidOperationException("Jogador nao encontrado na patota.");
            }
            else
            {
                // Como o convidado da partida: criado na patota, para poder voltar em outros jogos.
                if (string.IsNullOrWhiteSpace(dto.Name))
                    throw new InvalidOperationException("Nome do convidado e obrigatorio.");
                if (dto.GuestStarRating is < 1 or > 5)
                    throw new InvalidOperationException("A nota do convidado deve ser de 1 a 5.");

                player = new PlayerEntity(dto.Name.Trim(), null, groupId, 0m, dto.IsGoalkeeper, isGuest: true, Status.Active);
                if (dto.GuestStarRating.HasValue)
                    player.SetGuestStarRating(dto.GuestStarRating.Value);
                _db.Players.Add(player);
            }
            c.AddConfirmedPlayer(player);
        });

    // ── Times ────────────────────────────────────────────────────────────────

    /// <summary>Sugere times equilibrados, sem salvar, usando a mesma força do sorteio da patota.</summary>
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

        var colors = (await ActiveColorsShuffledAsync(groupId, ct)).Take(teamCount).ToList();

        var players = confirmed
            .Select(participant => new PlayerRequestDto(
                participant.PlayerId,
                participant.Player!.Name,
                participant.IsGoalkeeper,
                participant.Player.GuestStarRating))
            .ToList();
        var goalkeeperIds = confirmed
            .Where(participant => participant.IsGoalkeeper)
            .Select(participant => participant.PlayerId)
            .ToList();
        var draw = await _multiTeamGeneration.GenerateAsync(
            players, teamCount, MultiTeamStrategy.Balanced, goalkeeperIds, ct: ct);
        if (!draw.Success)
        {
            return Result<DefineTeamsDto>.Fail(
                draw.Error ?? "Nao foi possivel sortear os times.", draw.Status, draw.Errors);
        }

        var option = draw.Data?.Options.FirstOrDefault();
        if (option is null)
            return Result<DefineTeamsDto>.Fail("Nao foi possivel montar uma opcao de times.");

        var teams = option.Teams.Select((team, i) => new DefineTeamDto(
            i < colors.Count ? colors[i].Name : $"Time {i + 1}",
            i < colors.Count ? colors[i].Id : null,
            team.PlayerIds.ToList(),
            team.GoalkeeperPlayerId)).ToList();

        return Result<DefineTeamsDto>.Ok(new DefineTeamsDto(teams), ColorShortageWarning(teamCount, colors.Count));
    }

    /// <summary>
    /// Replays do campeonato por jogo, na ordem dos jogos. Com <paramref name="groupId"/> nulo é a
    /// leitura pública do modo cinema (como a da partida, pelo id).
    /// </summary>
    public async Task<Result<ChampionshipReplaysDto>> GetReplaysAsync(Guid? groupId, Guid championshipId, CancellationToken ct)
    {
        var championship = await _db.Championships.AsNoTracking()
            .Include(c => c.Teams)
            .FirstOrDefaultAsync(c => c.Id == championshipId && (groupId == null || c.GroupId == groupId), ct);
        if (championship is null) return NotFound<ChampionshipReplaysDto>();

        var games = await GamesQuery().AsNoTracking()
            .Where(m => m.ChampionshipId == championshipId)
            .OrderBy(m => m.ChampionshipGameOrder)
            .ToListAsync(ct);
        var gameIds = games.Select(g => g.Id).ToList();
        var clips = await _db.ReplayClips.AsNoTracking()
            .Where(c => gameIds.Contains(c.MatchId))
            .OrderBy(c => c.RecordedAt)
            .ToListAsync(ct);
        var colors = await _db.TeamColors.AsNoTracking()
            .Where(c => c.GroupId == championship.GroupId)
            .ToDictionaryAsync(c => c.Id, c => c.HexValue, ct);
        var teams = championship.Teams.ToDictionary(t => t.Id);
        string Name(Guid id) => teams.TryGetValue(id, out var t) ? t.Name : "Time";
        string? Hex(Guid id) => teams.TryGetValue(id, out var t) && t.ColorId.HasValue ? colors.GetValueOrDefault(t.ColorId.Value) : null;

        var result = games.Select(game =>
        {
            int order = game.ChampionshipGameOrder ?? 0, a = 0, b = 0;
            var gameClips = clips.Where(c => c.MatchId == game.Id).Select(c =>
            {
                if (c.EventType is MatchEventType.GolTimeA) a++;
                else if (c.EventType is MatchEventType.GolTimeB) b++;
                var teamId = c.EventType switch
                {
                    MatchEventType.GolTimeA => game.ChampionshipTeamAId,
                    MatchEventType.GolTimeB => game.ChampionshipTeamBId,
                    _ => null,
                };
                return new ChampionshipReplayClipDto(
                    c.Id, _replayUrls?.GeneratePresignedUrl(c.ObjectKey) ?? "", c.EventType.ToString(), c.RecordedAt,
                    teamId, a, b, $"Jogo {order} - {a} x {b}");
            }).ToList();
            return new ChampionshipGameReplaysDto(
                game.Id, order,
                game.ChampionshipTeamAId!.Value, Name(game.ChampionshipTeamAId.Value), Hex(game.ChampionshipTeamAId.Value),
                game.ChampionshipTeamBId!.Value, Name(game.ChampionshipTeamBId.Value), Hex(game.ChampionshipTeamBId.Value),
                game.TeamAGoals, game.TeamBGoals, gameClips);
        }).ToList();

        return Result<ChampionshipReplaysDto>.Ok(new ChampionshipReplaysDto(
            championship.Id, championship.Name, championship.PlayedAt, championship.PlaceName, result));
    }

    /// <summary>Vincula (ou desvincula, com null) uma votação/evento da patota, como na partida.</summary>
    public Task<Result> SetLinkedPollAsync(Guid groupId, Guid championshipId, Guid? pollId, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, async c =>
        {
            if (pollId.HasValue && !await _db.Polls.AnyAsync(p => p.Id == pollId.Value && p.GroupId == groupId, ct))
                throw new InvalidOperationException("Votacao nao encontrada nesta patota.");
            c.SetLinkedPoll(pollId);
        });

    public Task<Result> OpenTeamDrawAsync(Guid groupId, Guid championshipId, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, c =>
        {
            c.OpenTeamDraw();
            return Task.CompletedTask;
        });

    public Task<Result> SetPlayerGoalkeeperAsync(Guid groupId, Guid championshipId, Guid playerId, bool isGoalkeeper, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, c =>
        {
            c.SetPlayerGoalkeeper(playerId, isGoalkeeper);
            return Task.CompletedTask;
        });

    /// <summary>
    /// Sorteio da etapa Sorteio (v8): mesmos métodos da partida, sem salvar. As cores vêm da
    /// aba Cores (na ordem dos times) ou são sorteadas; o nome do time é o da cor.
    /// </summary>
    public async Task<Result<ChampionshipDrawOptionsDto>> DrawTeamOptionsAsync(
        Guid groupId, Guid championshipId, ChampionshipDrawRequestDto dto, CancellationToken ct)
    {
        if (_teamDraw is null)
            return Result<ChampionshipDrawOptionsDto>.Fail("Sorteio indisponivel.");
        var championship = await LoadAsync(groupId, championshipId, tracking: false, ct);
        if (championship is null) return NotFound<ChampionshipDrawOptionsDto>();
        if (championship.Status is not (ChampionshipStatus.Acceptation or ChampionshipStatus.TeamDraw))
            return Result<ChampionshipDrawOptionsDto>.Fail("So e possivel sortear durante a presenca ou o sorteio.");
        if (dto.TeamCount < championship.Rules.MinimumTeams)
            return Result<ChampionshipDrawOptionsDto>.Fail($"Esse formato precisa de ao menos {championship.Rules.MinimumTeams} times.");

        var confirmed = championship.Players
            .Where(p => p.InviteResponse == InviteResponse.Accepted && !p.DidNotPlay && p.Player is not null)
            .Select(p => new PlayerRequestDto(p.PlayerId, p.Player!.Name, p.IsGoalkeeper, p.Player.GuestStarRating))
            .ToList();

        // Cores escolhidas valem na ordem; as que faltarem saem do sorteio entre as restantes.
        var active = await ActiveColorsShuffledAsync(groupId, ct);
        var chosen = (dto.ColorIds ?? []).Take(dto.TeamCount).ToList();
        if (chosen.Where(id => id.HasValue).GroupBy(id => id).Any(g => g.Count() > 1))
            return Result<ChampionshipDrawOptionsDto>.Fail("Dois times nao podem ter a mesma cor.");
        if (chosen.Any(id => id.HasValue && active.All(c => c.Id != id)))
            return Result<ChampionshipDrawOptionsDto>.Fail("Cor de time invalida: use uma cor ativa desta patota.");
        var remaining = active.Where(c => !chosen.Contains(c.Id)).ToList();
        var colors = new List<TeamColorEntity?>();
        for (var i = 0; i < dto.TeamCount; i++)
        {
            if (i < chosen.Count && chosen[i].HasValue)
                colors.Add(active.First(c => c.Id == chosen[i]));
            else if (remaining.Count > 0)
            {
                colors.Add(remaining[0]);
                remaining.RemoveAt(0);
            }
            else
                colors.Add(null);
        }

        var draw = await _teamDraw.GenerateAsync(
            confirmed, dto.TeamCount, dto.StrategyType, dto.IncludeGoalkeepers, dto.OptionsCount, ct);
        if (!draw.Success)
            return Result<ChampionshipDrawOptionsDto>.Fail(draw.Error ?? "Nao foi possivel sortear os times.", draw.Status, draw.Errors);

        var options = draw.Data!.Select(option => new ChampionshipDrawOptionDto(
            option.Teams.Select((team, i) => new DefineTeamDto(
                colors[i]?.Name ?? $"Time {i + 1}",
                colors[i]?.Id,
                team.PlayerIds.ToList(),
                team.GoalkeeperPlayerId)).ToList(),
            option.UnassignedPlayerIds.ToList(),
            option.Teams.Select(t => t.Strength).ToList(),
            option.StrengthDifference)).ToList();

        return Result<ChampionshipDrawOptionsDto>.Ok(
            new ChampionshipDrawOptionsDto(options),
            ColorShortageWarning(dto.TeamCount, colors.Count(c => c is not null)));
    }

    public Task<Result> DefineTeamsAsync(Guid groupId, Guid championshipId, DefineTeamsDto dto, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, async c =>
        {
            await EnsureActiveGroupColorsAsync(groupId, dto.Teams.Select(t => t.ColorId), ct);

            c.DefineTeams(dto.Teams
                .Select(t => new ChampionshipEntity.TeamSpec(t.Name, t.ColorId, t.PlayerIds ?? [], t.GoalkeeperPlayerId))
                .ToList());

            // Times novos: as apostas abertas apontavam para os times antigos.
            await Bets.DiscardOpenBetsAsync(championshipId, ct);
        });

    public Task<Result> MovePlayerAsync(Guid groupId, Guid championshipId, Guid playerId, Guid? teamId, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, c =>
        {
            c.MovePlayer(playerId, teamId);
            return Task.CompletedTask;
        });

    /// <summary>
    /// Sorteia as cores dos times entre as cores ativas da patota, sem repetir. Com menos cores
    /// que times, os que sobram ficam sem cor e a resposta traz o aviso. Vale para os próximos jogos.
    /// </summary>
    public async Task<Result> DrawTeamColorsAsync(Guid groupId, Guid championshipId, CancellationToken ct)
    {
        string? warning = null;
        var result = await Mutate(groupId, championshipId, ct, async c =>
        {
            var colors = await ActiveColorsShuffledAsync(groupId, ct);
            var teams = c.Teams.OrderBy(t => t.Order).ToList();
            c.SetTeamColors(teams
                .Select((team, i) => (team.Id, Color: i < colors.Count ? colors[i].Id : (Guid?)null))
                .ToDictionary(x => x.Id, x => x.Color));
            warning = ColorShortageWarning(teams.Count, colors.Count);
        });

        return result.Success ? Result.Ok(warning ?? "Cores sorteadas.") : result;
    }

    public Task<Result> UpdateTeamAsync(Guid groupId, Guid championshipId, Guid teamId, UpdateChampionshipTeamDto dto, CancellationToken ct) =>
        Mutate(groupId, championshipId, ct, async c =>
        {
            await EnsureActiveGroupColorsAsync(groupId, [dto.ColorId], ct);
            c.UpdateTeam(teamId, dto.Name, dto.ColorId);
        });

    private async Task<List<TeamColorEntity>> ActiveColorsShuffledAsync(Guid groupId, CancellationToken ct)
    {
        var colors = await _db.TeamColors.AsNoTracking()
            .Where(c => c.GroupId == groupId && c.IsActive)
            .ToListAsync(ct);
        return colors.OrderBy(_ => Random.Shared.Next()).ToList();
    }

    /// <summary>Só cores ativas da própria patota podem ser usadas.</summary>
    private async Task EnsureActiveGroupColorsAsync(Guid groupId, IEnumerable<Guid?> colorIds, CancellationToken ct)
    {
        var ids = colorIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0) return;

        var valid = await _db.TeamColors.CountAsync(tc => tc.GroupId == groupId && tc.IsActive && ids.Contains(tc.Id), ct);
        if (valid != ids.Count)
            throw new InvalidOperationException("Cor de time invalida: use uma cor ativa desta patota.");
    }

    /// <summary>Aviso para o admin quando há menos cores ativas que times.</summary>
    private static string? ColorShortageWarning(int teamCount, int colorCount) =>
        colorCount >= teamCount
            ? null
            : $"Ha so {colorCount} cor(es) ativa(s) para {teamCount} times: {teamCount - colorCount} time(s) ficaram sem cor. " +
              "Cadastre mais cores em Cores dos times.";

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

    public async Task<Result> AddGoalAsync(Guid groupId, Guid championshipId, Guid gameId, ChampionshipGoalDto dto, CancellationToken ct)
    {
        var result = await AddGoalCoreAsync(groupId, championshipId, gameId, dto, ct);
        return result.Success ? await SyncLiveScoreAsync(gameId, ct) : result;
    }

    private Task<Result> AddGoalCoreAsync(Guid groupId, Guid championshipId, Guid gameId, ChampionshipGoalDto dto, CancellationToken ct) =>
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

    public async Task<Result> RemoveGoalAsync(Guid groupId, Guid championshipId, Guid gameId, Guid goalId, CancellationToken ct)
    {
        var result = await MutateGame(groupId, championshipId, gameId, ct, (_, game) =>
        {
            var goal = game.Goals.FirstOrDefault(g => g.Id == goalId)
                ?? throw new InvalidOperationException("Gol nao encontrado.");
            game.RemoveGoal(goalId);
            _db.Goals.Remove(goal);
        });
        return result.Success ? await SyncLiveScoreAsync(gameId, ct) : result;
    }

    /// <summary>
    /// Placar ao vivo = o maior entre os gols com autor e os botões "Gol Time A/B" das câmeras,
    /// por time. Assim apertar o botão e depois dizer quem fez conta um gol só.
    /// </summary>
    private async Task<Result> SyncLiveScoreAsync(Guid gameId, CancellationToken ct)
    {
        var game = await GamesQuery().Include(m => m.Players).Include(m => m.Goals)
            .FirstOrDefaultAsync(m => m.Id == gameId, ct);
        if (game is null || game.Status != MatchStatus.Started) return Result.Ok();

        var buttons = await _db.ChampionshipGameEvents.AsNoTracking()
            .Where(e => e.GameId == gameId && (e.Type == MatchEventType.GolTimeA || e.Type == MatchEventType.GolTimeB))
            .GroupBy(e => e.Type)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var teamByMatchPlayer = game.Players.ToDictionary(p => p.Id, p => p.Team);
        int byGoals(int team) => game.Goals.Count(g =>
            teamByMatchPlayer.TryGetValue(g.ScorerMatchPlayerId, out var t) && (g.IsOwnGoal ? t != team : t == team));

        game.SetChampionshipLiveScore(
            Math.Max(byGoals(1), buttons.GetValueOrDefault(MatchEventType.GolTimeA)),
            Math.Max(byGoals(2), buttons.GetValueOrDefault(MatchEventType.GolTimeB)));
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

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

    public async Task<Result> FinalizeAsync(Guid groupId, Guid championshipId, FinalizeChampionshipDto dto, CancellationToken ct)
    {
        var result = await Mutate(groupId, championshipId, ct, async c =>
        {
            var games = await LoadGamesAsync(championshipId, tracking: true, ct);
            var positions = FinalPositions(c, games, dto.ExternalDecisionTeamOrder);

            var settings = await _db.GroupSettings.AsNoTracking().FirstOrDefaultAsync(s => s.GroupId == groupId, ct);
            c.Finalize(positions, games, settings?.MvpTieRule ?? MvpTieRule.AllMvp, settings?.MvpTieMaxPlayers ?? 2);
        });

        // Finalizado, o campeonato passa a contar nas estatísticas gerais (conforme as regras).
        if (result.Success && _projection is not null)
            await _projection.RebuildGroupAsync(groupId, ct);

        // Classificação e MVP definitivos: liquida as apostas.
        if (result.Success)
            await Bets.ResolveAsync(championshipId, ct);

        return result;
    }

    // ── Voltar etapa e lances ────────────────────────────────────────────────

    public async Task<Result> StepBackAsync(Guid groupId, Guid championshipId, CancellationToken ct)
    {
        var wasFinalized = false;
        var result = await Mutate(groupId, championshipId, ct, async c =>
        {
            wasFinalized = c.Status == ChampionshipStatus.Finalized;
            var clearsTeams = c.Status == ChampionshipStatus.TeamDraw && c.Teams.Count > 0;
            var games = await LoadGamesAsync(championshipId, tracking: true, ct);
            c.StepBack(games);

            // Times desfeitos: as apostas abertas apontavam para eles.
            if (clearsTeams)
                await Bets.DiscardOpenBetsAsync(championshipId, ct);
        });

        // Reaberto, o resultado deixa de ser definitivo: estorna as apostas liquidadas.
        if (result.Success && wasFinalized)
            await Bets.UnresolveAsync(championshipId, ct);

        // Reaberto, o campeonato deixa de contar nas estatísticas até ser finalizado de novo.
        if (result.Success && wasFinalized && _projection is not null)
            await _projection.RebuildGroupAsync(groupId, ct);

        return result;
    }

    /// <summary>
    /// Marca um lance para as câmeras. O pedido de recorte vai pelo mesmo publicador das
    /// partidas, com matchId = id do jogo (o jogo é uma partida); o lance fica gravado com
    /// campeonato, jogo, time e o tempo do jogo para achar a gravação depois.
    /// </summary>
    public async Task<Result<ChampionshipGameEventCreatedDto>> PublishGameEventAsync(
        Guid groupId, Guid championshipId, Guid gameId, Guid? userId, ChampionshipGameEventRequestDto dto, CancellationToken ct)
    {
        if (dto.Type is not (MatchEventType.GolTimeA or MatchEventType.GolTimeB or MatchEventType.Jogada))
            return Result<ChampionshipGameEventCreatedDto>.Fail("Tipo de lance invalido: use GolTimeA, GolTimeB ou Jogada.");
        if (_eventPublisher is null)
            return Result<ChampionshipGameEventCreatedDto>.Fail("Marcacao de lances indisponivel neste ambiente.");

        var championship = await _db.Championships.AsNoTracking()
            .FirstOrDefaultAsync(c => c.GroupId == groupId && c.Id == championshipId, ct);
        if (championship is null) return NotFound<ChampionshipGameEventCreatedDto>();

        var game = await GamesQuery().AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == gameId && m.ChampionshipId == championshipId, ct);
        if (game is null)
            return Result<ChampionshipGameEventCreatedDto>.Fail("Jogo nao encontrado neste campeonato.", ResultStatus.NotFound);
        if (game.Status != MatchStatus.Started)
            return Result<ChampionshipGameEventCreatedDto>.Fail("Esse jogo nao esta em andamento.");

        var eventTime = (dto.EventTime ?? DateTimeOffset.UtcNow).UtcDateTime;
        int? elapsed = game.ActualStartTime is { } start
            ? Math.Max(0, (int)(eventTime - DateTime.SpecifyKind(start, DateTimeKind.Utc)).TotalSeconds)
            : null;
        var teamId = dto.Type switch
        {
            MatchEventType.GolTimeA => game.ChampionshipTeamAId,
            MatchEventType.GolTimeB => game.ChampionshipTeamBId,
            _ => null
        };

        string queueId;
        try
        {
            queueId = await _eventPublisher.PublishAsync(groupId, gameId, dto.Type, dto.SecondsBeforeStart,
                dto.DurationSeconds, new DateTimeOffset(eventTime, TimeSpan.Zero), ct);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ChampionshipGameEventCreatedDto>.Fail("Nao foi possivel avisar as cameras: " + ex.Message);
        }

        var entity = new ChampionshipGameEventEntity(
            championshipId, gameId, dto.Type, teamId, eventTime, elapsed, queueId, userId);
        _db.ChampionshipGameEvents.Add(entity);
        await _db.SaveChangesAsync(ct);

        // Como na partida: o botão de gol já soma o gol no time.
        if (teamId.HasValue)
            await SyncLiveScoreAsync(gameId, ct);

        return Result<ChampionshipGameEventCreatedDto>.Ok(
            new ChampionshipGameEventCreatedDto(entity.Id, queueId, teamId, entity.EventTimeUtc, elapsed),
            status: ResultStatus.Created);
    }

    // ── Formato: próximo jogo, empates e colocação final ─────────────────────

    private static NextGameSuggestionDto? SuggestNextGame(ChampionshipEntity c, IReadOnlyList<MatchEntity> games)
    {
        var teamIds = OrderedTeamIds(c);
        if (teamIds.Count < 2) return null;

        var history = FinishedHistory(games);
        var rules = c.Rules;
        var names = c.Teams.ToDictionary(t => t.Id, t => t.Name);

        // Não há fim automático: o admin encerra o campeonato pelo botão. A sugestão só
        // acaba quando o próprio formato acaba (tabela e final jogadas).
        try
        {
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
            ChampionshipFormat.WinnerStays => rules.WinnerStays?.TieRule == WinnerStaysTieRule.PenaltyShootout,
            ChampionshipFormat.RoundRobinWithFinal =>
                (game.ChampionshipGameOrder ?? 0) > RoundRobinScheduleGenerator
                    .Generate(OrderedTeamIds(c), rules.RoundRobinTurns).Sum(r => r.Games.Count),
            _ => false
        };
    }

    /// <summary>
    /// Posição final de cada time. Todos contra todos + final: os dois da final e depois a
    /// classificação. Demais: classificação.
    /// </summary>
    private static Dictionary<Guid, int> FinalPositions(
        ChampionshipEntity c, IReadOnlyList<MatchEntity> games, IReadOnlyList<Guid>? externalOrder)
    {
        var teamIds = OrderedTeamIds(c);
        var history = FinishedHistory(games);
        var rules = c.Rules;
        var standings = StandingsCalculator.Calculate(teamIds, history, rules.ToStandingsRules(), externalOrder);
        var byStanding = standings.ToDictionary(s => s.TeamId, s => s.Position);

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
