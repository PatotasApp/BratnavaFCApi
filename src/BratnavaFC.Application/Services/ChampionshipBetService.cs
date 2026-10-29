using BratnavaFC.Domain.Championships;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Championships;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Apostas do campeonato (docs/campeonato/DECISOES.md, seção "Apostas").
/// - Sempre no campeonato inteiro; os jogos não aceitam aposta.
/// - Abrem com os times definidos (TeamDraw) e fecham quando o campeonato inicia.
/// - Mesmo saldo, limites e bônus de participação das apostas de partida.
/// - Liquidadas ao finalizar; voltar de Finalizado desfaz a liquidação.
/// - Empate numa categoria: quem apostou em qualquer um dos empatados acerta.
/// </summary>
public sealed class ChampionshipBetService
{
    /// <summary>Acertar um jogador (artilheiro, assistente, goleiro, MVP) paga 3x o apostado.</summary>
    public const decimal PlayerCategoryMultiplier = 3m;

    private readonly AppDbContext _db;

    public ChampionshipBetService(AppDbContext db) => _db = db;

    /// <summary>Acertar o campeão paga (times − 1) vezes o apostado: com 3 times, 2x; com 4, 3x.</summary>
    public static decimal ChampionMultiplier(int teamCount) => Math.Max(1, teamCount - 1);

    // ── Consulta ─────────────────────────────────────────────────────────────

    public async Task<List<ChampionshipBettableDto>> GetBettableAsync(Guid groupId, CancellationToken ct)
    {
        var open = await _db.Championships.AsNoTracking()
            .Where(c => c.GroupId == groupId && c.Status == ChampionshipStatus.TeamDraw && c.Teams.Count >= 2)
            .OrderBy(c => c.PlayedAt)
            .Select(c => new ChampionshipBettableDto(c.Id, c.Name, c.PlayedAt, c.PlaceName))
            .ToListAsync(ct);
        return open;
    }

    public async Task<Result<ChampionshipBetContextDto>> GetContextAsync(Guid groupId, Guid championshipId, Guid userId, CancellationToken ct)
    {
        var c = await LoadChampionshipAsync(groupId, championshipId, ct);
        if (c is null) return Result<ChampionshipBetContextDto>.Fail("Campeonato nao encontrado.", ResultStatus.NotFound);

        var bets = await _db.ChampionshipBets.AsNoTracking()
            .Include(b => b.Selections)
            .Where(b => b.ChampionshipId == championshipId)
            .ToListAsync(ct);

        var isResolved = c.Status == ChampionshipStatus.Finalized && bets.All(b => b.IsResolved);
        var winners = isResolved ? await ComputeWinnersAsync(c, ct) : null;
        var names = await UserNamesAsync(bets.Select(b => b.UserId), ct);
        var optionNames = OptionNames(c);

        var balance = await _db.Set<UserBetBalanceEntity>().AsNoTracking()
            .Where(b => b.GroupId == groupId && b.UserId == userId)
            .Select(b => (int?)b.Balance)
            .FirstOrDefaultAsync(ct) ?? 0;

        var mine = bets.FirstOrDefault(b => b.UserId == userId);
        var isOpen = IsOpen(c, out var closedReason);

        return Result<ChampionshipBetContextDto>.Ok(new ChampionshipBetContextDto(
            c.Id, c.Name, c.PlayedAt, isOpen, closedReason, isResolved,
            BetService.MinWagerPerCategory, BetService.MaxWagerPerMatch, BetService.ParticipationBonus,
            balance,
            Categories(c, winners),
            mine is null ? null : ToUserBet(mine, names, optionNames),
            // Com as apostas abertas, ninguém vê a aposta dos outros.
            isOpen ? [] : bets.Select(b => ToUserBet(b, names, optionNames))
                .OrderByDescending(b => b.TotalForChampionship ?? 0).ToList()));
    }

    public async Task<PagedResultDto<ChampionshipBetHistoryDto>> GetHistoryAsync(Guid groupId, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        var query = _db.Championships.AsNoTracking()
            .Where(c => c.GroupId == groupId && c.Status == ChampionshipStatus.Finalized
                        && _db.ChampionshipBets.Any(b => b.ChampionshipId == c.Id && b.IsResolved));

        var total = await query.CountAsync(ct);
        var ids = await query.OrderByDescending(c => c.PlayedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var items = new List<ChampionshipBetHistoryDto>(ids.Count);
        foreach (var id in ids)
        {
            var c = (await LoadChampionshipAsync(groupId, id, ct))!;
            var bets = await _db.ChampionshipBets.AsNoTracking()
                .Include(b => b.Selections)
                .Where(b => b.ChampionshipId == id && b.IsResolved)
                .ToListAsync(ct);
            var names = await UserNamesAsync(bets.Select(b => b.UserId), ct);
            var optionNames = OptionNames(c);
            items.Add(new ChampionshipBetHistoryDto(c.Id, c.Name, c.PlayedAt,
                bets.Select(b => ToUserBet(b, names, optionNames)).OrderByDescending(b => b.TotalForChampionship ?? 0).ToList()));
        }

        return new PagedResultDto<ChampionshipBetHistoryDto> { Page = page, PageSize = pageSize, Total = total, Items = items };
    }

    // ── Apostar ──────────────────────────────────────────────────────────────

    public async Task<Result> PlaceOrUpdateAsync(Guid groupId, Guid championshipId, Guid userId, PlaceChampionshipBetDto dto, CancellationToken ct)
    {
        var c = await LoadChampionshipAsync(groupId, championshipId, ct);
        if (c is null) return Result.Fail("Campeonato nao encontrado.", ResultStatus.NotFound);
        if (!IsOpen(c, out var closedReason)) return Result.Fail(closedReason!);

        var error = Validate(c, userId, dto);
        if (error is not null) return Result.Fail(error);

        var bet = await _db.ChampionshipBets
            .Include(b => b.Selections)
            .FirstOrDefaultAsync(b => b.ChampionshipId == championshipId && b.UserId == userId, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (bet is null)
        {
            bet = new ChampionshipBetEntity(groupId, championshipId, userId);
            _db.ChampionshipBets.Add(bet);
        }
        else
        {
            _db.ChampionshipBetSelections.RemoveRange(bet.Selections.ToList());
        }
        await _db.SaveChangesAsync(ct);

        var selections = dto.Selections
            .Select(s => new ChampionshipBetSelectionEntity(bet.Id, s.Category, s.PredictedId, s.FichasWagered))
            .ToList();
        bet.ReplaceSelections(selections);
        _db.ChampionshipBetSelections.AddRange(selections);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Result.Ok();
    }

    public async Task<Result> DeleteAsync(Guid groupId, Guid championshipId, Guid userId, CancellationToken ct)
    {
        var c = await LoadChampionshipAsync(groupId, championshipId, ct);
        if (c is null) return Result.Fail("Campeonato nao encontrado.", ResultStatus.NotFound);
        if (!IsOpen(c, out var closedReason)) return Result.Fail(closedReason!);

        var bet = await _db.ChampionshipBets
            .FirstOrDefaultAsync(b => b.ChampionshipId == championshipId && b.UserId == userId, ct);
        if (bet is null) return Result.Fail("Voce nao tem aposta neste campeonato.", ResultStatus.NotFound);

        _db.ChampionshipBets.Remove(bet);
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    private static string? Validate(ChampionshipEntity c, Guid userId, PlaceChampionshipBetDto dto)
    {
        var selections = dto.Selections ?? [];
        if (selections.Count < 1 || selections.Count > 5)
            return "Escolha entre 1 e 5 categorias.";
        if (selections.Select(s => s.Category).Distinct().Count() != selections.Count)
            return "Cada categoria so pode ser apostada uma vez.";
        if (selections.All(s => s.Category != ChampionshipBetCategory.Champion))
            return "A categoria 'Time campeao' e obrigatoria.";
        if (selections.Any(s => s.FichasWagered < BetService.MinWagerPerCategory))
            return $"Minimo de {BetService.MinWagerPerCategory} Bratnava Coins por categoria.";

        var total = selections.Sum(s => s.FichasWagered);
        if (total > BetService.MaxWagerPerMatch)
            return $"Total apostado ({total}) nao pode ultrapassar {BetService.MaxWagerPerMatch} Bratnava Coins por campeonato.";

        var available = Categories(c, null).ToDictionary(x => x.Category);
        foreach (var s in selections)
        {
            if (!available.TryGetValue(s.Category, out var category))
                return s.Category == ChampionshipBetCategory.Mvp
                    ? "Este campeonato nao tem MVP."
                    : "Categoria invalida.";
            if (category.Options.All(o => o.Id != s.PredictedId))
                return s.Category == ChampionshipBetCategory.Champion
                    ? "Time invalido para este campeonato."
                    : "Jogador invalido para esta categoria.";

            if (s.Category != ChampionshipBetCategory.Champion
                && c.Players.Any(p => p.PlayerId == s.PredictedId && p.Player?.UserId == userId))
                return "Voce nao pode apostar em si mesmo.";
        }

        return null;
    }

    private static bool IsOpen(ChampionshipEntity c, out string? closedReason)
    {
        closedReason = c.Status switch
        {
            ChampionshipStatus.TeamDraw when c.Teams.Count >= 2 => null,
            ChampionshipStatus.Created or ChampionshipStatus.Acceptation or ChampionshipStatus.TeamDraw
                => "As apostas abrem quando os times forem definidos.",
            ChampionshipStatus.Cancelled => "Campeonato cancelado.",
            _ => "As apostas fecharam quando o campeonato comecou."
        };
        return closedReason is null;
    }

    // ── Liquidação (chamada pelo ChampionshipService) ────────────────────────

    /// <summary>Liquida as apostas de um campeonato recém-finalizado e credita o saldo.</summary>
    public async Task ResolveAsync(Guid championshipId, CancellationToken ct)
    {
        var c = await _db.Championships.AsNoTracking()
            .Include(x => x.Teams)
            .Include(x => x.Players).ThenInclude(p => p.Player)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == championshipId, ct);
        if (c is null || c.Status != ChampionshipStatus.Finalized) return;

        var bets = await _db.ChampionshipBets
            .Include(b => b.Selections)
            .Where(b => b.ChampionshipId == championshipId && !b.IsResolved)
            .ToListAsync(ct);
        if (bets.Count == 0) return;

        var winners = await ComputeWinnersAsync(c, ct);
        var championMultiplier = ChampionMultiplier(c.Teams.Count);

        foreach (var bet in bets)
        {
            foreach (var selection in bet.Selections)
            {
                var categoryWinners = winners.GetValueOrDefault(selection.Category, []);
                var multiplier = selection.Category == ChampionshipBetCategory.Champion ? championMultiplier : PlayerCategoryMultiplier;
                var earned = categoryWinners.Contains(selection.PredictedId)
                    ? (int)(selection.FichasWagered * multiplier)
                    : -(int)(selection.FichasWagered * 0.5m);
                selection.Resolve(categoryWinners, earned);
            }

            bet.MarkResolved();
            var balance = await GetOrCreateBalanceAsync(bet.GroupId, bet.UserId, ct);
            balance.ApplyDelta(Delta(bet));
            balance.RecordBetResult(bet.Selections.Count(s => s.IsCorrect == true));
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Desfaz a liquidação (o admin voltou de Finalizado): estorna o saldo.</summary>
    public async Task UnresolveAsync(Guid championshipId, CancellationToken ct)
    {
        var bets = await _db.ChampionshipBets
            .Include(b => b.Selections)
            .Where(b => b.ChampionshipId == championshipId && b.IsResolved)
            .ToListAsync(ct);
        if (bets.Count == 0) return;

        foreach (var bet in bets)
        {
            var balance = await GetOrCreateBalanceAsync(bet.GroupId, bet.UserId, ct);
            balance.ReverseBetResult(bet.Selections.Count(s => s.IsCorrect == true), Delta(bet));
            bet.Unresolve();
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Apaga as apostas abertas: os times mudaram (as apostas apontavam para eles) ou o campeonato acabou cancelado.</summary>
    public async Task DiscardOpenBetsAsync(Guid championshipId, CancellationToken ct)
    {
        await _db.ChampionshipBets
            .Where(b => b.ChampionshipId == championshipId && !b.IsResolved)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Soma do que as apostas de campeonato liquidadas renderam, por (patota, usuário). Usado no recálculo de saldos.</summary>
    public async Task<Dictionary<(Guid GroupId, Guid UserId), int>> ResolvedTotalsAsync(CancellationToken ct)
    {
        var bets = await _db.ChampionshipBets.AsNoTracking()
            .Include(b => b.Selections)
            .Where(b => b.IsResolved)
            .ToListAsync(ct);
        return bets.GroupBy(b => (b.GroupId, b.UserId)).ToDictionary(g => g.Key, g => g.Sum(Delta));
    }

    private static int Delta(ChampionshipBetEntity bet) =>
        BetService.ParticipationBonus + bet.Selections.Sum(s => s.FichasEarned ?? 0);

    // ── Vencedores de cada categoria ─────────────────────────────────────────

    private async Task<Dictionary<ChampionshipBetCategory, IReadOnlyCollection<Guid>>> ComputeWinnersAsync(ChampionshipEntity c, CancellationToken ct)
    {
        // Única leitura dos jogos aqui; eles ficam fora das consultas comuns (filtro global).
        var games = await _db.Matches.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.ChampionshipId == c.Id && m.Status == MatchStatus.Finalized)
            .Include(m => m.Players)
            .Include(m => m.Goals)
            .AsSplitQuery()
            .ToListAsync(ct);

        var playerOf = games.SelectMany(g => g.Players).ToDictionary(mp => mp.Id, mp => mp.PlayerId);
        var goals = games.SelectMany(g => g.Goals).Where(g => !g.IsOwnGoal).ToList();

        var result = new Dictionary<ChampionshipBetCategory, IReadOnlyCollection<Guid>>
        {
            [ChampionshipBetCategory.Champion] = c.Teams.Where(t => t.FinalPosition == 1).Select(t => t.Id).ToList(),
            [ChampionshipBetCategory.TopScorer] = Leaders(goals.Select(g => playerOf[g.ScorerMatchPlayerId])),
            [ChampionshipBetCategory.TopAssister] = Leaders(goals
                .Where(g => g.AssistMatchPlayerId.HasValue)
                .Select(g => playerOf[g.AssistMatchPlayerId!.Value])),
            [ChampionshipBetCategory.LeastConcededGoalkeeper] = LeastConceded(games),
            [ChampionshipBetCategory.Mvp] = c.Rules.MvpMode == MvpMode.PerGame
                ? Leaders(games.SelectMany(g => g.Players).Where(mp => mp.IsMvp == true).Select(mp => mp.PlayerId))
                : c.Players.Where(p => p.IsMvp).Select(p => p.PlayerId).ToList()
        };
        return result;
    }

    /// <summary>Quem mais aparece (empate = todos). Ninguém com 1 ou mais: lista vazia.</summary>
    private static List<Guid> Leaders(IEnumerable<Guid> occurrences)
    {
        var counts = occurrences.GroupBy(id => id).Select(g => (Id: g.Key, Count: g.Count())).ToList();
        if (counts.Count == 0) return [];
        var max = counts.Max(x => x.Count);
        return counts.Where(x => x.Count == max).Select(x => x.Id).ToList();
    }

    /// <summary>
    /// Goleiro menos vazado: menor média de gols sofridos por jogo no gol. Vale o IsGoalkeeper
    /// de cada jogo, e só concorre quem esteve no gol em pelo menos metade dos jogos do time.
    /// </summary>
    private static List<Guid> LeastConceded(IReadOnlyList<MatchEntity> games)
    {
        var stints = games
            .SelectMany(g => g.Players
                .Where(mp => mp.IsGoalkeeper && !mp.DidNotPlay && mp.Team is 1 or 2)
                .Select(mp => (
                    mp.PlayerId,
                    TeamId: mp.Team == 1 ? g.ChampionshipTeamAId : g.ChampionshipTeamBId,
                    Conceded: mp.Team == 1 ? g.TeamBGoals ?? 0 : g.TeamAGoals ?? 0,
                    Order: g.ChampionshipGameOrder ?? 0)))
            .ToList();

        var teamGames = games
            .SelectMany(g => new[] { g.ChampionshipTeamAId, g.ChampionshipTeamBId })
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        var eligible = stints
            .GroupBy(s => s.PlayerId)
            .Select(g =>
            {
                var team = g.OrderBy(s => s.Order).Last().TeamId;
                return (Id: g.Key, Games: g.Count(), Average: g.Average(s => s.Conceded),
                    TeamGames: teamGames.GetValueOrDefault(team, g.Count()));
            })
            .Where(x => x.Games * 2 >= x.TeamGames)
            .ToList();

        if (eligible.Count == 0) return [];
        var best = eligible.Min(x => x.Average);
        return eligible.Where(x => Math.Abs(x.Average - best) < 1e-9).Select(x => x.Id).ToList();
    }

    // ── Montagem ─────────────────────────────────────────────────────────────

    private static List<ChampionshipBetCategoryDto> Categories(
        ChampionshipEntity c, Dictionary<ChampionshipBetCategory, IReadOnlyCollection<Guid>>? winners)
    {
        IReadOnlyList<Guid>? Winners(ChampionshipBetCategory category) =>
            winners is null ? null : winners.GetValueOrDefault(category, []).ToList();

        var teams = c.Teams.OrderBy(t => t.Order)
            .Select(t => new ChampionshipBetOptionDto(t.Id, t.Name, t.Color?.HexValue, t.Id))
            .ToList();

        var players = c.Players
            .Where(p => p.TeamId.HasValue && !p.DidNotPlay)
            .OrderBy(p => p.Player?.Name)
            .ToList();
        ChampionshipBetOptionDto Option(ChampionshipPlayerEntity p) => new(
            p.PlayerId, p.Player?.Name ?? "", c.Teams.FirstOrDefault(t => t.Id == p.TeamId)?.Color?.HexValue, p.TeamId);

        var everyone = players.Select(Option).ToList();
        var goalkeepers = players.Where(p => p.IsGoalkeeper).Select(Option).ToList();

        var list = new List<ChampionshipBetCategoryDto>
        {
            new(ChampionshipBetCategory.Champion, ChampionMultiplier(c.Teams.Count), teams, Winners(ChampionshipBetCategory.Champion)),
            new(ChampionshipBetCategory.LeastConcededGoalkeeper, PlayerCategoryMultiplier, goalkeepers, Winners(ChampionshipBetCategory.LeastConcededGoalkeeper)),
            new(ChampionshipBetCategory.TopScorer, PlayerCategoryMultiplier, everyone, Winners(ChampionshipBetCategory.TopScorer)),
            new(ChampionshipBetCategory.TopAssister, PlayerCategoryMultiplier, everyone, Winners(ChampionshipBetCategory.TopAssister)),
        };
        if (c.Rules.MvpMode != MvpMode.None)
            list.Add(new(ChampionshipBetCategory.Mvp, PlayerCategoryMultiplier, everyone, Winners(ChampionshipBetCategory.Mvp)));
        return list;
    }

    private static Dictionary<Guid, string> OptionNames(ChampionshipEntity c) =>
        c.Teams.Select(t => (t.Id, t.Name))
            .Concat(c.Players.Select(p => (p.PlayerId, p.Player?.Name ?? "")))
            .GroupBy(x => x.Item1)
            .ToDictionary(g => g.Key, g => g.First().Item2);

    private static ChampionshipUserBetDto ToUserBet(
        ChampionshipBetEntity bet, Dictionary<Guid, string> userNames, Dictionary<Guid, string> optionNames) => new(
        bet.UserId,
        userNames.GetValueOrDefault(bet.UserId, "Usuário"),
        bet.IsResolved,
        bet.Selections.OrderBy(s => s.Category)
            .Select(s => new ChampionshipBetSelectionDto(
                s.Category, s.PredictedId, optionNames.GetValueOrDefault(s.PredictedId, ""),
                s.FichasWagered, s.FichasEarned, s.IsCorrect))
            .ToList(),
        bet.IsResolved ? Delta(bet) : null);

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
    }

    private Task<ChampionshipEntity?> LoadChampionshipAsync(Guid groupId, Guid championshipId, CancellationToken ct) =>
        _db.Championships.AsNoTracking()
            .Where(c => c.GroupId == groupId && c.Id == championshipId)
            .Include(c => c.Teams).ThenInclude(t => t.Color)
            .Include(c => c.Players).ThenInclude(p => p.Player)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct);

    private async Task<UserBetBalanceEntity> GetOrCreateBalanceAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var balance = _db.Set<UserBetBalanceEntity>().Local.FirstOrDefault(b => b.GroupId == groupId && b.UserId == userId)
            ?? await _db.Set<UserBetBalanceEntity>().FirstOrDefaultAsync(b => b.GroupId == groupId && b.UserId == userId, ct);
        if (balance is null)
        {
            balance = new UserBetBalanceEntity(groupId, userId);
            _db.Set<UserBetBalanceEntity>().Add(balance);
        }
        return balance;
    }
}
