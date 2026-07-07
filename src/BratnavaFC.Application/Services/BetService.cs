using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Constants;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class BetService : IBetService
{
    private readonly AppDbContext _db;

    /// <summary>Bônus de participação creditado a quem apostou, independente de acertos.</summary>
    private const int ParticipationBonus = 50;
    /// <summary>Total máximo de fichas que pode ser apostado por partida.</summary>
    private const int MaxWagerPerMatch = 200;

    public BetService(AppDbContext db) => _db = db;

    // ── Contexto da aposta atual ──────────────────────────────────────────────

    public async Task<CurrentMatchBetContextDto?> GetCurrentContextAsync(
        Guid groupId, Guid userId, CancellationToken ct)
    {
        var match = await _db.Matches
            .AsNoTracking()
            .Include(m => m.TeamAColor)
            .Include(m => m.TeamBColor)
            .Where(m => m.GroupId == groupId &&
                        m.Status != MatchStatus.Finalized &&
                        m.Status != MatchStatus.Created)
            .OrderByDescending(m => m.PlayedAt)
            .FirstOrDefaultAsync(ct);

        if (match is null) return null;
        return await BuildContextAsync(match, userId, ct);
    }

    public async Task<CurrentMatchBetContextDto?> GetContextForMatchAsync(
        Guid groupId, Guid matchId, Guid userId, CancellationToken ct)
    {
        var match = await _db.Matches
            .AsNoTracking()
            .Include(m => m.TeamAColor)
            .Include(m => m.TeamBColor)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null) return null;
        return await BuildContextAsync(match, userId, ct);
    }

    // ── Lista de partidas apostáveis (carousel) ───────────────────────────────

    public async Task<List<BettableMatchDto>> GetBettableMatchesAsync(
        Guid groupId, CancellationToken ct)
    {
        var matches = await _db.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.MatchMaking)
            .OrderBy(m => m.PlayedAt)
            .ToListAsync(ct);

        if (matches.Count == 0) return [];

        var matchIds = matches.Select(m => m.Id).ToList();

        // Single query: which matches have at least one player in each team
        var teamsByMatch = await _db.MatchPlayers
            .AsNoTracking()
            .Where(mp => matchIds.Contains(mp.MatchId) && (mp.Team == 1 || mp.Team == 2))
            .GroupBy(mp => mp.MatchId)
            .Select(g => new { MatchId = g.Key, Teams = g.Select(mp => mp.Team).Distinct().ToList() })
            .ToListAsync(ct);

        var bettableIds = teamsByMatch
            .Where(x => x.Teams.Contains((short)1) && x.Teams.Contains((short)2))
            .Select(x => x.MatchId)
            .ToHashSet();

        return matches
            .Where(m => bettableIds.Contains(m.Id))
            .Select(m => new BettableMatchDto(m.Id, m.PlayedAt, m.PlaceName))
            .ToList();
    }

    // ── Apaga todas as apostas não-resolvidas de uma partida ─────────────────

    public async Task ResetBetsForMatchAsync(Guid matchId, CancellationToken ct)
    {
        var bets = await _db.Set<MatchBetEntity>()
            .Include(b => b.Selections)
            .Where(b => b.MatchId == matchId && !b.IsResolved)
            .ToListAsync(ct);

        if (bets.Count == 0) return;

        _db.Set<MatchBetSelectionEntity>().RemoveRange(bets.SelectMany(b => b.Selections).ToList());
        _db.Set<MatchBetEntity>().RemoveRange(bets);
        await _db.SaveChangesAsync(ct);
    }

    // ── Helper: monta o contexto para um MatchEntity já carregado ────────────

    private async Task<CurrentMatchBetContextDto> BuildContextAsync(
        MatchEntity match, Guid userId, CancellationToken ct)
    {
        // UserId de quem já apostou + total de fichas apostadas
        var betTotals = await _db.Set<MatchBetEntity>()
            .AsNoTracking()
            .Where(b => b.MatchId == match.Id)
            .Select(b => new { b.UserId, Total = (int?)b.Selections.Sum(s => s.FichasWagered) })
            .ToListAsync(ct);

        var bettedUserIds = betTotals.Select(x => x.UserId).ToHashSet();
        var wageredByUser = betTotals.ToDictionary(x => x.UserId, x => x.Total);

        var rawPlayers = await _db.MatchPlayers
            .AsNoTracking()
            .Include(mp => mp.Player)
            .Where(mp => mp.MatchId == match.Id)
            .ToListAsync(ct);

        var players = rawPlayers.Select(mp =>
        {
            var hasBet = mp.Player?.UserId != null && bettedUserIds.Contains(mp.Player.UserId.Value);
            var fichas = hasBet && mp.Player?.UserId != null
                ? wageredByUser.GetValueOrDefault(mp.Player.UserId.Value)
                : null;
            return new BetPlayerDto(mp.Id, mp.PlayerId, mp.Player!.Name, mp.Team,
                mp.Player?.IsGuest ?? false, hasBet, fichas);
        }).ToList();

        // Membros do grupo adicionados depois da escalação ser fechada: aparecem com Team=0
        var matchPlayerIds = rawPlayers.Select(mp => mp.PlayerId).ToHashSet();
        var lateMembers = await _db.Players
            .AsNoTracking()
            .Where(p => p.GroupId == match.GroupId &&
                        !p.IsGuest &&
                        p.Status == Domain.Enums.Status.Active &&
                        !matchPlayerIds.Contains(p.Id))
            .ToListAsync(ct);

        foreach (var p in lateMembers)
        {
            var hasBet = p.UserId != null && bettedUserIds.Contains(p.UserId.Value);
            var fichas = hasBet && p.UserId != null
                ? wageredByUser.GetValueOrDefault(p.UserId.Value)
                : null;
            players.Add(new BetPlayerDto(p.Id, p.Id, p.Name, 0, false, hasBet, fichas));
        }

        var myBet = await GetMyBetDtoAsync(match.Id, userId, ct);

        // Bet window is open only during MatchMaking AND teams have been assigned
        var hasTeams = rawPlayers.Any(mp => mp.Team == 1) && rawPlayers.Any(mp => mp.Team == 2);
        var betWindowOpen = match.Status == MatchStatus.MatchMaking && hasTeams;

        return new CurrentMatchBetContextDto(
            match.Id,
            match.PlayedAt,
            match.Status.ToString(),
            betWindowOpen,
            match.TeamAColor?.Name,
            match.TeamBColor?.Name,
            match.TeamAColor?.HexValue,
            match.TeamBColor?.HexValue,
            players,
            myBet);
    }

    // ── Criar / atualizar aposta ──────────────────────────────────────────────

    public async Task<Result> PlaceOrUpdateBetAsync(
        Guid groupId, Guid matchId, Guid userId, PlaceMatchBetDto dto, CancellationToken ct)
    {
        var match = await _db.Matches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
            return Result.Fail("Partida não encontrada.");

        if (match.Status != MatchStatus.MatchMaking)
            return Result.Fail("As apostas só podem ser feitas durante o período de matchmaking.");

        if (dto.Selections.Count < 1 || dto.Selections.Count > 5)
            return Result.Fail("Escolha entre 1 e 5 categorias.");

        var hasWinningTeam = dto.Selections.Any(s =>
            s.Category.Equals("WinningTeam", StringComparison.OrdinalIgnoreCase));

        if (!hasWinningTeam)
            return Result.Fail("A categoria 'Time vencedor' é obrigatória.");

        foreach (var sel in dto.Selections)
        {
            if (sel.FichasWagered < 30)
                return Result.Fail("Mínimo de 30 Bratnava Coins por categoria.");

            var validationError = ValidatePredictedValue(sel.Category, sel.PredictedValue);
            if (validationError is not null)
                return Result.Fail(validationError);
        }

        var totalWagered = dto.Selections.Sum(s => s.FichasWagered);
        if (totalWagered > MaxWagerPerMatch)
            return Result.Fail($"Total apostado ({totalWagered}) não pode ultrapassar {MaxWagerPerMatch} Bratnava Coins por partida.");

        // Validação cruzada: placar deve ser consistente com o vencedor
        var winSel   = dto.Selections.FirstOrDefault(s => s.Category.Equals("WinningTeam", StringComparison.OrdinalIgnoreCase));
        var scoreSel = dto.Selections.FirstOrDefault(s => s.Category.Equals("FinalScore",  StringComparison.OrdinalIgnoreCase));
        if (winSel is not null && scoreSel is not null)
        {
            var parts  = scoreSel.PredictedValue.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var sA) && int.TryParse(parts[1], out var sB))
            {
                var consistent = winSel.PredictedValue switch
                {
                    "TeamA" => sA > sB,
                    "TeamB" => sB > sA,
                    "Draw"  => sA == sB,
                    _       => true,
                };
                if (!consistent)
                    return Result.Fail("O placar apostado é incompatível com o time vencedor escolhido.");
            }
        }

        // Jogador não pode apostar em si mesmo (PlayerGoals / PlayerAssists)
        var playerSelections = dto.Selections
            .Where(s => s.Category.Equals("PlayerGoals",   StringComparison.OrdinalIgnoreCase) ||
                        s.Category.Equals("PlayerAssists", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (playerSelections.Count > 0)
        {
            var callerMatchPlayerId = await _db.Set<MatchPlayerEntity>()
                .AsNoTracking()
                .Where(mp => mp.MatchId == matchId && mp.Player!.UserId == userId)
                .Select(mp => (Guid?)mp.Id)
                .FirstOrDefaultAsync(ct);

            if (callerMatchPlayerId.HasValue)
            {
                foreach (var sel in playerSelections)
                {
                    var parts = sel.PredictedValue.Split('|');
                    if (parts.Length >= 1 &&
                        Guid.TryParse(parts[0], out var selectedMatchPlayerId) &&
                        selectedMatchPlayerId == callerMatchPlayerId.Value)
                    {
                        return Result.Fail("Voce nao pode apostar em si mesmo.");
                    }
                }
            }
        }

        // Times devem estar definidos para que a aposta seja válida
        var teamFlags = await _db.MatchPlayers
            .Where(mp => mp.MatchId == matchId && (mp.Team == 1 || mp.Team == 2))
            .Select(mp => mp.Team)
            .Distinct()
            .ToListAsync(ct);

        if (!teamFlags.Contains((short)1) || !teamFlags.Contains((short)2))
            return Result.Fail("Os times ainda não foram definidos para esta partida.");

        // Criar ou atualizar aposta — dentro de uma transação para garantir atomicidade
        var bet = await _db.Set<MatchBetEntity>()
            .Include(b => b.Selections)
            .FirstOrDefaultAsync(b => b.MatchId == matchId && b.UserId == userId, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (bet is null)
            {
                bet = new MatchBetEntity(groupId, matchId, userId);
                _db.Set<MatchBetEntity>().Add(bet);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                _db.Set<MatchBetSelectionEntity>().RemoveRange(bet.Selections.ToList());
                await _db.SaveChangesAsync(ct);
            }

            var newSelections = dto.Selections.Select(s => new MatchBetSelectionEntity(
                bet.Id,
                ParseCategory(s.Category),
                s.PredictedValue.Trim(),
                s.FichasWagered
            )).ToList();

            bet.ReplaceSelections(newSelections);
            _db.Set<MatchBetSelectionEntity>().AddRange(newSelections);
            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        return Result.Ok();
    }

    // ── Deletar aposta ────────────────────────────────────────────────────────

    public async Task<Result> DeleteBetAsync(
        Guid groupId, Guid matchId, Guid userId, CancellationToken ct)
    {
        var match = await _db.Matches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
            return Result.Fail("Partida não encontrada.");

        if (match.Status != MatchStatus.MatchMaking)
            return Result.Fail("As apostas só podem ser removidas durante o matchmaking.");

        var deleteTeamFlags = await _db.MatchPlayers
            .Where(mp => mp.MatchId == matchId && (mp.Team == 1 || mp.Team == 2))
            .Select(mp => mp.Team)
            .Distinct()
            .ToListAsync(ct);

        if (!deleteTeamFlags.Contains((short)1) || !deleteTeamFlags.Contains((short)2))
            return Result.Fail("Os times ainda não foram definidos para esta partida.");

        var bet = await _db.Set<MatchBetEntity>()
            .Include(b => b.Selections)
            .FirstOrDefaultAsync(b => b.MatchId == matchId && b.UserId == userId, ct);

        if (bet is null)
            return Result.Fail("Nenhuma aposta encontrada para este usuário.");

        _db.Set<MatchBetSelectionEntity>().RemoveRange(bet.Selections.ToList());
        _db.Set<MatchBetEntity>().Remove(bet);
        await _db.SaveChangesAsync(ct);

        return Result.Ok();
    }

    // ── Resultados de uma partida ─────────────────────────────────────────────

    public async Task<MatchBetResultsDto?> GetMatchResultsAsync(
        Guid groupId, Guid matchId, CancellationToken ct)
    {
        var match = await _db.Matches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null) return null;

        // Resolução lazy
        if (match.Status == MatchStatus.Finalized)
        {
            var hasUnresolved = await _db.Set<MatchBetEntity>()
                .AnyAsync(b => b.MatchId == matchId && !b.IsResolved, ct);
            if (hasUnresolved)
                await ResolveBetsForMatchAsync(match, ct);
        }

        var bets = await _db.Set<MatchBetEntity>()
            .AsNoTracking()
            .Include(b => b.Selections)
            .Where(b => b.MatchId == matchId)
            .ToListAsync(ct);

        if (bets.Count == 0)
            return new MatchBetResultsDto(matchId, match.Status == MatchStatus.Finalized, []);

        var userIds   = bets.Select(b => b.UserId).Distinct().ToList();
        var userNames = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var balances = await _db.Set<UserBetBalanceEntity>().AsNoTracking()
            .Where(b => b.GroupId == groupId && userIds.Contains(b.UserId))
            .ToDictionaryAsync(b => b.UserId, b => b.Balance, ct);

        var userBets = bets.Select(bet =>
        {
            var sels = ToSelectionDtos(bet.Selections);
            var betEarnings = bet.IsResolved ? sels.Sum(s => s.FichasEarned ?? 0) : 0;
            var total = bet.IsResolved ? ParticipationBonus + betEarnings : 0;

            return new UserBetResultDto(
                bet.UserId,
                userNames.GetValueOrDefault(bet.UserId, "Usuário"),
                sels,
                total,
                balances.GetValueOrDefault(bet.UserId, 0)
            );
        }).OrderByDescending(u => u.TotalFichasEarned).ToList();

        return new MatchBetResultsDto(matchId, match.Status == MatchStatus.Finalized, userBets);
    }

    // ── Histórico ─────────────────────────────────────────────────────────────

    public async Task<PagedResultDto<MatchBetHistoryDto>> GetHistoryAsync(Guid groupId, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        // Resolve any bets for finalized matches that were never explicitly resolved.
        // This prevents history from appearing empty just because GetMatchResults
        // was never called for those matches.
        await EnsureResolvedForGroupAsync(groupId, ct);

        // Pagina por partida (cada item do histórico agrupa as apostas de uma partida)
        var matchesQuery = _db.Matches
            .AsNoTracking()
            .Where(m => _db.Set<MatchBetEntity>()
                .Any(b => b.GroupId == groupId && b.IsResolved && b.MatchId == m.Id));

        var total = await matchesQuery.CountAsync(ct);

        var matches = await matchesQuery
            .OrderByDescending(m => m.PlayedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var matchIds = matches.Select(m => m.Id).ToList();

        if (matchIds.Count == 0)
            return new PagedResultDto<MatchBetHistoryDto> { Page = page, PageSize = pageSize, Total = total, Items = [] };

        var allBets = await _db.Set<MatchBetEntity>()
            .AsNoTracking()
            .Include(b => b.Selections)
            .Where(b => matchIds.Contains(b.MatchId) && b.IsResolved)
            .ToListAsync(ct);

        var allUserIds = allBets.Select(b => b.UserId).Distinct().ToList();
        var userNames  = await _db.Users.AsNoTracking()
            .Where(u => allUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        // Resolve nomes dos jogadores apostados (PlayerGoals / PlayerAssists)
        var playerBetCategories = new[] { BetCategory.PlayerGoals, BetCategory.PlayerAssists };
        var matchPlayerIdStrings = allBets
            .SelectMany(b => b.Selections)
            .Where(s => playerBetCategories.Contains(s.Category))
            .Select(s => s.PredictedValue.Split('|').FirstOrDefault())
            .Where(id => Guid.TryParse(id, out _))
            .Select(id => Guid.Parse(id!))
            .Distinct()
            .ToList();

        var playerNames = matchPlayerIdStrings.Count > 0
            ? await _db.MatchPlayers.AsNoTracking()
                .Where(mp => matchPlayerIdStrings.Contains(mp.Id))
                .Select(mp => new { Id = mp.Id.ToString(), mp.Player!.Name })
                .ToDictionaryAsync(mp => mp.Id, mp => mp.Name, ct)
            : new Dictionary<string, string>();

        var items = matches.Select(match =>
        {
            var bets     = allBets.Where(b => b.MatchId == match.Id).ToList();
            var userBets = bets.Select(bet =>
            {
                var sels        = ToSelectionDtos(bet.Selections, playerNames);
                var betEarnings = sels.Sum(s => s.FichasEarned ?? 0);
                return new UserBetInHistoryDto(
                    bet.UserId,
                    userNames.GetValueOrDefault(bet.UserId, "Usuário"),
                    bet.CreateDate,
                    sels,
                    ParticipationBonus,
                    betEarnings,
                    ParticipationBonus + betEarnings
                );
            }).OrderByDescending(u => u.TotalForMatch).ToList();

            return new MatchBetHistoryDto(
                match.Id,
                match.PlayedAt,
                match.TeamAGoals ?? 0,
                match.TeamBGoals ?? 0,
                userBets
            );
        }).ToList();

        return new PagedResultDto<MatchBetHistoryDto> { Page = page, PageSize = pageSize, Total = total, Items = items };
    }

    // ── Leaderboard ──────────────────────────────────────────────────────────

    public async Task<List<BetLeaderboardEntryDto>> GetLeaderboardAsync(
        Guid groupId, CancellationToken ct)
    {
        await EnsureResolvedForGroupAsync(groupId, ct);

        var balances = await _db.Set<UserBetBalanceEntity>()
            .AsNoTracking()
            .Where(b => b.GroupId == groupId)
            .ToListAsync(ct);

        var userIds   = balances.Select(b => b.UserId).Distinct().ToList();
        var userNames = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        return balances
            .OrderByDescending(b => b.Balance)
            .ThenByDescending(b => b.TotalCorrect)
            .Select((b, i) => new BetLeaderboardEntryDto(
                i + 1,
                b.UserId,
                userNames.GetValueOrDefault(b.UserId, "Usuário"),
                b.Balance,
                b.TotalBets,
                b.TotalCorrect
            )).ToList();
    }

    // ── Saldo pessoal ─────────────────────────────────────────────────────────

    public async Task<int> GetMyBalanceAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var balance = await _db.Set<UserBetBalanceEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.GroupId == groupId && b.UserId == userId, ct);
        return balance?.Balance ?? 0;
    }

    // ── Preview parcial (sem persistência) ───────────────────────────────────

    public async Task<BetPreviewDto?> GetBetPreviewAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var match = await _db.Matches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.Id == matchId, ct);

        if (match is null) return null;

        var bets = await _db.Set<MatchBetEntity>()
            .AsNoTracking()
            .Include(b => b.Selections)
            .Where(b => b.MatchId == matchId && !b.IsResolved)
            .ToListAsync(ct);

        var scoreA = match.TeamAGoals ?? 0;
        var scoreB = match.TeamBGoals ?? 0;

        if (bets.Count == 0)
            return new BetPreviewDto(matchId, scoreA, scoreB, []);

        var actualWinner = scoreA > scoreB ? "TeamA"
                         : scoreB > scoreA ? "TeamB"
                         : "Draw";
        var actualScore  = $"{scoreA}:{scoreB}";

        var goals = await _db.Goals
            .AsNoTracking()
            .Where(g => g.MatchId == matchId && !g.IsOwnGoal)
            .ToListAsync(ct);

        var goalsByPlayer   = goals
            .GroupBy(g => g.ScorerMatchPlayerId)
            .ToDictionary(g => g.Key, g => g.Count());
        var assistsByPlayer = goals
            .Where(g => g.AssistMatchPlayerId.HasValue)
            .GroupBy(g => g.AssistMatchPlayerId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var userIds   = bets.Select(b => b.UserId).Distinct().ToList();
        var userNames = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var userBets = bets.Select(bet =>
        {
            var simSels = bet.Selections.Select(sel =>
            {
                string actualValue = sel.Category switch
                {
                    BetCategory.WinningTeam => actualWinner,
                    BetCategory.FinalScore  => actualScore,
                    BetCategory.PlayerGoals or BetCategory.PlayerAssists => BuildPlayerActualValue(
                        sel.PredictedValue,
                        sel.Category == BetCategory.PlayerGoals ? goalsByPlayer : assistsByPlayer),
                    _ => ""
                };

                var (fichasEarned, isCorrect, isPartial) =
                    CalculateEarnings(sel.Category, sel.PredictedValue, actualValue, sel.FichasWagered);

                return new BetSelectionDto(sel.Id, sel.Category.ToString(), sel.PredictedValue,
                    actualValue, sel.FichasWagered, fichasEarned, isCorrect, isPartial);
            }).ToList();

            var betEarnings = simSels.Sum(s => s.FichasEarned ?? 0);
            return new BetPreviewUserDto(
                bet.UserId,
                userNames.GetValueOrDefault(bet.UserId, "Usuário"),
                simSels,
                betEarnings,
                ParticipationBonus + betEarnings);

        }).OrderByDescending(u => u.SimulatedTotal).ToList();

        return new BetPreviewDto(matchId, scoreA, scoreB, userBets, ParticipationBonus);
    }

    private static string BuildPlayerActualValue(string predictedValue, Dictionary<Guid, int> countByPlayer)
    {
        var parts = predictedValue.Split('|');
        if (parts.Length < 1 || !Guid.TryParse(parts[0], out var mpId))
            return predictedValue;
        return $"{parts[0]}|{countByPlayer.GetValueOrDefault(mpId, 0)}";
    }

    // ── Resolução ─────────────────────────────────────────────────────────────

    public async Task ResolveMatchBetsAsync(Guid matchId, CancellationToken ct)
    {
        var match = await _db.Matches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);

        if (match is null) return;

        var hasUnresolved = await _db.Set<MatchBetEntity>()
            .AnyAsync(b => b.MatchId == matchId && !b.IsResolved, ct);

        if (hasUnresolved)
            await ResolveBetsForMatchAsync(match, ct);
    }

    public async Task ReResolveMatchBetsAsync(Guid matchId, CancellationToken ct)
    {
        var match = await _db.Matches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);

        if (match is null) return;

        // Carrega apenas bets já resolvidas para reverter
        var resolvedBets = await _db.Set<MatchBetEntity>()
            .Include(b => b.Selections)
            .Where(b => b.MatchId == matchId && b.IsResolved)
            .ToListAsync(ct);

        if (resolvedBets.Count == 0)
        {
            // Nenhuma bet resolvida ainda — resolve normalmente
            await ResolveBetsForMatchAsync(match, ct);
            return;
        }

        var userIds = resolvedBets.Select(b => b.UserId).Distinct().ToList();
        var balances = await _db.Set<UserBetBalanceEntity>()
            .Where(b => b.GroupId == resolvedBets[0].GroupId && userIds.Contains(b.UserId))
            .ToDictionaryAsync(b => b.UserId, b => b, ct);

        // Reverte cada bet: desfaz crédito e re-marca como não resolvida
        foreach (var bet in resolvedBets)
        {
            var previousCorrect = bet.Selections.Count(s => s.IsCorrect == true);
            var previousDelta   = ParticipationBonus + bet.Selections.Sum(s => s.FichasEarned ?? 0);

            if (balances.TryGetValue(bet.UserId, out var balance))
                balance.ReverseBetResult(previousCorrect, previousDelta);

            foreach (var sel in bet.Selections)
                sel.Unresolve();

            bet.Unresolve();
        }

        await _db.SaveChangesAsync(ct);

        // Re-resolve com o placar atual
        await ResolveBetsForMatchAsync(match, ct);
    }

    /// <summary>
    /// Resolves all pending bets for finalized matches in the group.
    /// History and leaderboard queries call this so they never return stale
    /// empty data just because <c>GetMatchResultsAsync</c> was never explicitly hit.
    /// </summary>
    private async Task EnsureResolvedForGroupAsync(Guid groupId, CancellationToken ct)
    {
        var unresolvedMatchIds = await _db.Set<MatchBetEntity>()
            .AsNoTracking()
            .Where(b => b.GroupId == groupId && !b.IsResolved)
            .Select(b => b.MatchId)
            .Distinct()
            .ToListAsync(ct);

        if (unresolvedMatchIds.Count == 0) return;

        var finalizedMatches = await _db.Matches
            .Where(m => unresolvedMatchIds.Contains(m.Id) &&
                        m.Status == MatchStatus.Finalized)
            .ToListAsync(ct);

        foreach (var match in finalizedMatches)
            await ResolveBetsForMatchAsync(match, ct);
    }

    private async Task ResolveBetsForMatchAsync(MatchEntity match, CancellationToken ct)
    {
        var bets = await _db.Set<MatchBetEntity>()
            .Include(b => b.Selections)
            .Where(b => b.MatchId == match.Id && !b.IsResolved)
            .ToListAsync(ct);

        if (bets.Count == 0) return;

        var teamAGoals = match.TeamAGoals ?? 0;
        var teamBGoals = match.TeamBGoals ?? 0;
        var actualWinner = teamAGoals > teamBGoals ? "TeamA"
                         : teamBGoals > teamAGoals ? "TeamB"
                         : "Draw";
        var actualScore = $"{teamAGoals}:{teamBGoals}";

        var goals = await _db.Goals
            .AsNoTracking()
            .Where(g => g.MatchId == match.Id && !g.IsOwnGoal)
            .ToListAsync(ct);

        var goalsByPlayer   = goals
            .GroupBy(g => g.ScorerMatchPlayerId)
            .ToDictionary(g => g.Key, g => g.Count());
        var assistsByPlayer = goals
            .Where(g => g.AssistMatchPlayerId.HasValue)
            .GroupBy(g => g.AssistMatchPlayerId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var bet in bets)
        {
            int correctCount = 0;

            foreach (var sel in bet.Selections)
            {
                string actualValue;

                switch (sel.Category)
                {
                    case BetCategory.WinningTeam:
                        actualValue = actualWinner;
                        break;
                    case BetCategory.FinalScore:
                        actualValue = actualScore;
                        break;
                    case BetCategory.PlayerGoals:
                    {
                        var parts    = sel.PredictedValue.Split('|');
                        var mpId     = Guid.Parse(parts[0]);
                        var actCount = goalsByPlayer.GetValueOrDefault(mpId, 0);
                        actualValue  = $"{parts[0]}|{actCount}";
                        break;
                    }
                    case BetCategory.PlayerAssists:
                    {
                        var parts    = sel.PredictedValue.Split('|');
                        var mpId     = Guid.Parse(parts[0]);
                        var actCount = assistsByPlayer.GetValueOrDefault(mpId, 0);
                        actualValue  = $"{parts[0]}|{actCount}";
                        break;
                    }
                    default:
                        actualValue = "";
                        break;
                }

                var (fichasEarned, isCorrect, isPartial) =
                    CalculateEarnings(sel.Category, sel.PredictedValue, actualValue, sel.FichasWagered);

                sel.Resolve(fichasEarned, isCorrect, isPartial, actualValue);
                if (isCorrect) correctCount++;
            }

            bet.MarkResolved();

            var balance  = await GetOrCreateBalanceAsync(bet.GroupId, bet.UserId, ct);
            var netDelta = ParticipationBonus + bet.Selections.Sum(s => s.FichasEarned ?? 0);
            balance.ApplyDelta(netDelta);
            balance.RecordBetResult(correctCount);
        }

        await _db.SaveChangesAsync(ct);
    }

    // ── Cálculo de fichas ─────────────────────────────────────────────────────

    private static (int fichasEarned, bool isCorrect, bool isPartial) CalculateEarnings(
        BetCategory category, string predicted, string actual, int wager)
    {
        switch (category)
        {
            case BetCategory.WinningTeam:
            {
                if (predicted == actual)
                {
                    var multiplier = predicted == "Draw" ? 2.5m : 1.0m;
                    return ((int)(wager * multiplier), true, false);
                }
                return (-(int)(wager * 0.5m), false, false);
            }

            case BetCategory.FinalScore:
            {
                if (predicted == actual) return ((int)(wager * 4.0m), true, false);

                var pParts = predicted.Split(':');
                var aParts = actual.Split(':');
                int pTotal = int.Parse(pParts[0]) + int.Parse(pParts[1]);
                int aTotal = int.Parse(aParts[0]) + int.Parse(aParts[1]);

                if (Math.Abs(pTotal - aTotal) <= 1)
                    return (0, false, true);

                return (-(int)(wager * 0.5m), false, false);
            }

            case BetCategory.PlayerGoals:
            case BetCategory.PlayerAssists:
            {
                var pParts = predicted.Split('|');
                var aParts = actual.Split('|');
                int pCount = int.Parse(pParts[1]);
                int aCount = int.Parse(aParts[1]);

                if (pCount == aCount) return ((int)(wager * 2.5m), true, false);
                if (Math.Abs(pCount - aCount) == 1) return (0, false, true);

                return (-(int)(wager * 0.5m), false, false);
            }

            default:
                return (0, false, false);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<BetSelectionDto> ToSelectionDtos(
        IEnumerable<MatchBetSelectionEntity> selections,
        Dictionary<string, string>? playerNames = null) =>
        selections.Select(s =>
        {
            string? playerName = null;
            if (playerNames is { Count: > 0 } &&
                (s.Category == BetCategory.PlayerGoals || s.Category == BetCategory.PlayerAssists))
            {
                var mpId = s.PredictedValue.Split('|').FirstOrDefault();
                if (mpId != null) playerNames.TryGetValue(mpId, out playerName);
            }
            return new BetSelectionDto(
                s.Id, s.Category.ToString(), s.PredictedValue, s.ActualValue,
                s.FichasWagered, s.FichasEarned, s.IsCorrect, s.IsPartialCredit,
                PlayerName: playerName);
        }).ToList();

    private async Task<MatchBetDto?> GetMyBetDtoAsync(Guid matchId, Guid userId, CancellationToken ct)
    {
        var bet = await _db.Set<MatchBetEntity>()
            .AsNoTracking()
            .Include(b => b.Selections)
            .FirstOrDefaultAsync(b => b.MatchId == matchId && b.UserId == userId, ct);

        if (bet is null) return null;

        var match    = await _db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
        var isLocked = match is not null && match.Status > MatchStatus.MatchMaking;
        var sels     = ToSelectionDtos(bet.Selections);

        return new MatchBetDto(
            bet.Id, bet.MatchId, bet.UserId, "",
            bet.IsResolved, isLocked, sels,
            bet.IsResolved ? ParticipationBonus + sels.Sum(s => s.FichasEarned ?? 0) : null
        );
    }

    private async Task<UserBetBalanceEntity> GetOrCreateBalanceAsync(
        Guid groupId, Guid userId, CancellationToken ct)
    {
        var balance = await _db.Set<UserBetBalanceEntity>()
            .FirstOrDefaultAsync(b => b.GroupId == groupId && b.UserId == userId, ct);

        if (balance is null)
        {
            balance = new UserBetBalanceEntity(groupId, userId);
            _db.Set<UserBetBalanceEntity>().Add(balance);
            // Não chamar SaveChangesAsync aqui: o chamador (ResolveBetsForMatchAsync)
            // persiste tudo de uma vez no SaveChangesAsync ao final do loop,
            // evitando múltiplos round-trips ao banco por usuário novo.
        }

        return balance;
    }

    private static BetCategory ParseCategory(string category) =>
        category.ToUpperInvariant() switch
        {
            "WINNINGTEAM"   => BetCategory.WinningTeam,
            "FINALSCORE"    => BetCategory.FinalScore,
            "PLAYERGOALS"   => BetCategory.PlayerGoals,
            "PLAYERASSISTS" => BetCategory.PlayerAssists,
            _               => throw new ArgumentException($"Categoria inválida: {category}"),
        };

    public async Task<int> RecalculateAllBalancesAsync(CancellationToken ct)
    {
        var balances = await _db.Set<UserBetBalanceEntity>().ToListAsync(ct);

        var allBets = await _db.Set<MatchBetEntity>()
            .Include(b => b.Selections)
            .Where(b => b.IsResolved)
            .ToListAsync(ct);

        // Agrupa por (GroupId, UserId) e soma só os FichasEarned das seleções
        var earnedByKey = allBets
            .GroupBy(b => (b.GroupId, b.UserId))
            .ToDictionary(
                g => g.Key,
                g => g.Count() * ParticipationBonus
                    + g.SelectMany(b => b.Selections).Sum(s => s.FichasEarned ?? 0));

        foreach (var bal in balances)
        {
            var correct = earnedByKey.TryGetValue((bal.GroupId, bal.UserId), out var earned) ? earned : 0;
            bal.ForceSetBalance(correct);
        }

        await _db.SaveChangesAsync(ct);
        return balances.Count;
    }

    private static string? ValidatePredictedValue(string category, string value)
    {
        var cat = category.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(value))
            return "PredictedValue não pode ser vazio.";

        return cat switch
        {
            "WINNINGTEAM" when value != "TeamA" && value != "TeamB" && value != "Draw"
                => "WinningTeam deve ser 'TeamA', 'TeamB' ou 'Draw'.",
            "FINALSCORE" when !value.Contains(':')
                => "FinalScore deve ter formato '{gols}:{gols}'.",
            "PLAYERGOALS" or "PLAYERASSISTS" when !value.Contains('|')
                => "PlayerGoals/PlayerAssists deve ter formato '{matchPlayerId}|{count}'.",
            _ => null,
        };
    }
}
