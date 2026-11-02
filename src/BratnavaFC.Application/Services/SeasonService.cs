using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Statistics;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Seasons;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Temporadas da patota (docs/temporadas/TEMPORADAS.md). A partida pertence à temporada
/// pela data do jogo; aqui só se mantém a lista de períodos encostados.
/// Virada do ano: automática só enquanto nenhum admin fechou temporada na patota.
/// </summary>
public sealed class SeasonService : ISeasonService
{
    /// <summary>Até quanto à frente o corte pode ser agendado.</summary>
    private static readonly TimeSpan MaxFutureCut = TimeSpan.FromDays(31);
    private static readonly TimeZoneInfo BrazilTz = ResolveBrazilTz();

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly IConquistaProjectionService? _titles;
    private readonly IPushService? _push;

    public SeasonService(AppDbContext db, TimeProvider? clock = null,
        IConquistaProjectionService? titles = null, IPushService? push = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _titles = titles;
        _push = push;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    // ── Consulta ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SeasonEntity>> GetSeasonsAsync(Guid groupId, CancellationToken ct) =>
        await EnsureSeasonsSafeAsync(groupId, ct);

    /// <summary>
    /// Fora de transação: se outra requisição completou/virou as temporadas ao mesmo tempo,
    /// o índice único recusa a cópia; basta ler de novo o que ela gravou.
    /// </summary>
    private async Task<List<SeasonEntity>> EnsureSeasonsSafeAsync(Guid groupId, CancellationToken ct)
    {
        try
        {
            return await EnsureSeasonsAsync(groupId, ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return await EnsureSeasonsAsync(groupId, ct);
        }
    }

    public async Task<Result<SeasonsOverviewDto>> ListAsync(Guid groupId, CancellationToken ct)
    {
        if (!await _db.Groups.AnyAsync(g => g.Id == groupId, ct))
            return Result<SeasonsOverviewDto>.Fail("Patota nao encontrada.", ResultStatus.NotFound);

        var seasons = await EnsureSeasonsSafeAsync(groupId, ct);
        // Virada automática ou corte agendado que já passou: sela e avisa.
        if (_titles is not null)
            await NotifyClosedAsync(groupId, await _titles.SealTitlesAsync(groupId, Now, ct), ct);
        var (matches, championships) = await LoadPlayedAtAsync(groupId, ct);

        var closerIds = seasons.Where(s => s.ClosedByUserId.HasValue).Select(s => s.ClosedByUserId!.Value).Distinct().ToList();
        var closers = await _db.Users.AsNoTracking()
            .Where(u => closerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var current = Current(seasons);
        var dtos = seasons.Select(s => ToDto(s, seasons, current, matches, championships, closers)).ToList();

        return Result<SeasonsOverviewDto>.Ok(new SeasonsOverviewDto(
            dtos, current?.Id, IsManual(seasons), UndoTarget(seasons) is not null));
    }

    public async Task<Result<SeasonClosePreviewDto>> PreviewCloseAsync(Guid groupId, DateTimeOffset? cutAt, CancellationToken ct) =>
        await Run(async () =>
        {
            var seasons = await EnsureSeasonsSafeAsync(groupId, ct);
            var open = OpenSeason(seasons);
            var cut = ValidateCut(open, cutAt);
            var (nextYear, nextNumber) = NextSlot(seasons, cut);
            var (matches, championships) = await LoadPlayedAtAsync(groupId, ct);

            // Depois do corte, o ano da temporada fechada pode ganhar a 2ª temporada: o nome muda.
            var seasonsInClosingYear = seasons.Count(s => s.Year == open.Year) + (nextYear == open.Year ? 1 : 0);
            var seasonsInNextYear = seasons.Count(s => s.Year == nextYear) + 1;

            return Result<SeasonClosePreviewDto>.Ok(new SeasonClosePreviewDto(
                open.Id,
                open.DisplayName(seasonsInClosingYear),
                open.StartsAt,
                cut,
                matches.Count(at => at >= open.StartsAt && at < cut),
                championships.Count(at => at >= open.StartsAt && at < cut),
                nextYear,
                nextNumber,
                seasonsInNextYear <= 1 ? $"{nextYear}" : $"{nextYear}/{nextNumber}",
                matches.Count(at => at >= cut),
                championships.Count(at => at >= cut)));
        });

    // ── Ações do admin ───────────────────────────────────────────────────────

    public async Task<Result> CloseAsync(Guid groupId, Guid? userId, CloseSeasonDto dto, CancellationToken ct)
    {
        SeasonEntity? closed = null;
        var result = await Mutate(groupId, ct, async seasons =>
        {
            var open = closed = OpenSeason(seasons);
            var cut = ValidateCut(open, dto.CutAt);
            var (nextYear, nextNumber) = NextSlot(seasons, cut);
            var next = new SeasonEntity(groupId, nextYear, nextNumber, cut, dto.NextName, dto.NextIsPreSeason);

            // Fecha primeiro e abre depois: o índice só aceita uma aberta por patota.
            open.Close(cut, userId, Now);
            await _db.SaveChangesAsync(ct);
            _db.Seasons.Add(next);
        });

        // Encerrada (corte já passou): sela os títulos e avisa a patota.
        if (result.Success && closed is not null)
        {
            // Só sela o que falta: a que fechou não tinha título, e os já selados não mudam.
            var closedId = closed.Id;
            var sealedNow = _titles is null ? [] : await _titles.SealTitlesAsync(groupId, Now, ct);
            await NotifyClosedAsync(groupId, sealedNow.Where(id => id == closedId).ToList(), ct);
        }
        return result;
    }

    public async Task<Result> UpdateAsync(Guid groupId, Guid seasonId, UpdateSeasonDto dto, CancellationToken ct)
    {
        var years = new List<int>();
        var result = await Mutate(groupId, ct, seasons =>
        {
            var season = Find(seasons, seasonId);
            season.Rename(dto.Name);
            // Pré-temporada não gera título: mudar a marcação refaz os títulos do ano.
            if (season.IsPreSeason != dto.IsPreSeason) years.Add(season.Year);
            season.SetPreSeason(dto.IsPreSeason);
            return Task.CompletedTask;
        });
        if (result.Success && years.Count > 0) await ResealAsync(groupId, years, ct);
        return result;
    }

    public async Task<Result> MoveStartAsync(Guid groupId, Guid seasonId, MoveSeasonStartDto dto, CancellationToken ct)
    {
        var years = new List<int>();
        var result = await Mutate(groupId, ct, seasons =>
        {
            var season = Find(seasons, seasonId);
            var previous = seasons.FirstOrDefault(s => s.EndsAt == season.StartsAt)
                ?? throw new InvalidOperationException("A primeira temporada comeca no inicio do ano e nao muda.");

            var start = dto.StartsAt.UtcDateTime;
            if (start <= previous.StartsAt)
                throw new InvalidOperationException("O corte tem de ser depois do inicio da temporada anterior.");
            var limit = season.EndsAt ?? Now + MaxFutureCut;
            if (start >= limit)
                throw new InvalidOperationException(season.IsOpen
                    ? "O corte pode ser agendado para no maximo 31 dias a frente."
                    : "O corte tem de ser antes do fim da temporada.");
            if (YearOf(start) != season.Year)
                throw new InvalidOperationException($"A temporada pertence a {season.Year}: o inicio tem de ficar nesse ano.");

            previous.MoveEnd(start);
            season.MoveStart(start);
            years.AddRange([previous.Year, season.Year]);
            return Task.CompletedTask;
        });
        if (result.Success) await ResealAsync(groupId, years, ct);
        return result;
    }

    public async Task<Result> UndoCloseAsync(Guid groupId, CancellationToken ct)
    {
        var years = new List<int>();
        var result = await Mutate(groupId, ct, async seasons =>
        {
            var previous = UndoTarget(seasons)
                ?? throw new InvalidOperationException("Nao ha fechamento feito pelo admin para desfazer.");
            var open = OpenSeason(seasons);
            years.AddRange([previous.Year, open.Year]);

            // Apaga a aberta primeiro: o índice só aceita uma aberta por patota.
            _db.Seasons.Remove(open);
            await _db.SaveChangesAsync(ct);
            previous.Reopen();
        });
        if (result.Success) await ResealAsync(groupId, years, ct);
        return result;
    }

    // ── Período das estatísticas ─────────────────────────────────────────────

    public async Task<Result<StatPeriod?>> ResolvePeriodAsync(Guid groupId, Guid? seasonId, int? year, bool includePreSeason, CancellationToken ct)
    {
        if (seasonId is null && year is null) return Result<StatPeriod?>.Ok(null);

        var seasons = await EnsureSeasonsSafeAsync(groupId, ct);
        if (seasonId is { } id)
        {
            var season = seasons.FirstOrDefault(s => s.Id == id);
            return season is null
                ? Result<StatPeriod?>.Fail("Temporada nao encontrada.", ResultStatus.NotFound)
                : Result<StatPeriod?>.Ok(new StatPeriod([new StatPeriodRange(season.StartsAt, season.EndsAt)], DisplayName(season, seasons)));
        }

        var ofYear = seasons.Where(s => s.Year == year && (includePreSeason || !s.IsPreSeason))
            .Select(s => new StatPeriodRange(s.StartsAt, s.EndsAt)).ToList();
        return Result<StatPeriod?>.Ok(new StatPeriod(ofYear, includePreSeason ? $"{year} (com pré-temporada)" : $"{year}"));
    }

    // ── Retrospectiva (fase 4) ───────────────────────────────────────────────

    public async Task<Result<SeasonSummaryDto>> GetSummaryAsync(Guid groupId, Guid seasonId, CancellationToken ct) =>
        await Run(async () =>
        {
            var seasons = await EnsureSeasonsSafeAsync(groupId, ct);
            var season = Find(seasons, seasonId);
            if (season.EndsAt is not { } end || end > Now)
                throw new InvalidOperationException("A retrospectiva sai quando a temporada fecha.");

            if (_titles is not null)
            {
                await _titles.EnsureGroupProjectedAsync(groupId, ct);
                await _titles.SealTitlesAsync(groupId, Now, ct);
            }

            var start = new DateTimeOffset(season.StartsAt, TimeSpan.Zero);
            var finish = new DateTimeOffset(end, TimeSpan.Zero);
            var rows = await _db.PlayerMatchStatContributions.AsNoTracking()
                .Where(c => c.GroupId == groupId && c.PlayedAt >= start && c.PlayedAt < finish)
                .ToListAsync(ct);
            var players = await _db.Players.AsNoTracking()
                .Where(p => p.GroupId == groupId)
                .Select(p => new SummaryPlayer(p.Id, p.Name, p.IsGuest, p.IsGoalkeeper))
                .ToDictionaryAsync(p => p.Id, ct);
            var stats = rows.GroupBy(r => r.PlayerId)
                .Where(g => players.ContainsKey(g.Key))
                .Select(g => new PlayerStatProjectionEntity(groupId, g.Key, season.Year, g))
                .ToList();

            var categories = ConquistaProjectionService.TitleCategories
                .Append(new ConquistaProjectionService.TitleCategory(
                    "CleanSheets", "Paredão", "Jogos sem sofrer gols", "🧤", x => x.CleanSheets, 0));
            var rankings = categories.Select(category => Ranking(category, stats, players)).ToList();

            var (matches, championships) = await LoadPlayedAtAsync(groupId, ct);
            var closers = season.ClosedByUserId is { } by
                ? await _db.Users.AsNoTracking().Where(u => u.Id == by).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct)
                : new Dictionary<Guid, string>();
            var titlesSealed = await _db.SeasonTitles.AsNoTracking().AnyAsync(t => t.SeasonId == season.Id, ct);

            return Result<SeasonSummaryDto>.Ok(new SeasonSummaryDto(
                ToDto(season, seasons, Current(seasons), matches, championships, closers),
                matches.Count(season.Contains), championships.Count(season.Contains),
                stats.Sum(p => p.Goals), stats.Count, titlesSealed, rankings));
        });

    /// <summary>Classificação real da categoria: empatados dividem a posição (1, 1, 3...).</summary>
    private static SeasonRankingDto Ranking(ConquistaProjectionService.TitleCategory category,
        IReadOnlyList<PlayerStatProjectionEntity> stats, IReadOnlyDictionary<Guid, SummaryPlayer> players)
    {
        var eligible = stats
            .Where(p => p.Games >= category.MinGames && category.Value(p) > 0)
            .Where(p => category.Category != "CleanSheets" || players[p.PlayerId].IsGoalkeeper)
            .OrderByDescending(category.Value).ThenByDescending(p => p.Games).ThenBy(p => players[p.PlayerId].Name)
            .ToList();
        var rows = eligible.Select(p => new SeasonRankingRowDto(
                eligible.Count(other => category.Value(other) > category.Value(p)) + 1,
                p.PlayerId, players[p.PlayerId].Name, category.Value(p), p.Games, players[p.PlayerId].IsGuest))
            .ToList();
        return new SeasonRankingDto(category.Category, category.RankingName, category.Icon, category.MinGames, rows);
    }

    private sealed record SummaryPlayer(Guid Id, string Name, bool IsGuest, bool IsGoalkeeper);

    // ── Títulos e aviso ──────────────────────────────────────────────────────

    private async Task<IReadOnlyList<Guid>> ResealAsync(Guid groupId, IEnumerable<int> years, CancellationToken ct) =>
        _titles is null ? [] : await _titles.ResealYearsAsync(groupId, years, Now, ct);

    /// <summary>Temporadas que acabaram de ganhar títulos: avisa a patota da retrospectiva.</summary>
    private async Task NotifyClosedAsync(Guid groupId, IReadOnlyCollection<Guid> seasonIds, CancellationToken ct)
    {
        if (_push is null || seasonIds.Count == 0) return;
        var seasons = await _db.Seasons.AsNoTracking().Where(s => s.GroupId == groupId).ToListAsync(ct);
        foreach (var season in seasons.Where(s => seasonIds.Contains(s.Id)))
        {
            await _push.SendToGroupAsync(
                groupId,
                title: $"Temporada {DisplayName(season, seasons)} encerrada",
                body: "Veja os campeões e a classificação final da temporada.",
                data: new Dictionary<string, string>
                {
                    ["type"] = "season_closed", ["groupId"] = groupId.ToString(), ["seasonId"] = season.Id.ToString(),
                },
                ct);
        }
    }

    // ── Cobertura e virada do ano ────────────────────────────────────────────

    /// <summary>
    /// Garante que as temporadas cobrem desde o primeiro jogo da patota e, sem admin no
    /// controle, vira o ano. Devolve as temporadas rastreadas, da mais antiga à mais nova.
    /// </summary>
    private async Task<List<SeasonEntity>> EnsureSeasonsAsync(Guid groupId, CancellationToken ct)
    {
        var seasons = await _db.Seasons.Where(s => s.GroupId == groupId).OrderBy(s => s.StartsAt).ToListAsync(ct);
        var currentYear = YearOf(Now);
        var changed = false;

        if (seasons.Count == 0)
        {
            var first = new SeasonEntity(groupId, currentYear, 1, StartOfYear(currentYear));
            _db.Seasons.Add(first);
            seasons.Add(first);
            changed = true;
        }

        // Jogo lançado antes da primeira temporada: completa os anos que faltam.
        var (matches, championships) = await LoadPlayedAtAsync(groupId, ct, finalizedOnly: false);
        var earliest = matches.Concat(championships).DefaultIfEmpty(seasons[0].StartsAt).Min();
        for (var year = seasons[0].Year - 1; year >= YearOf(earliest) && earliest < seasons[0].StartsAt; year--)
        {
            var season = new SeasonEntity(groupId, year, 1, StartOfYear(year));
            season.Close(seasons[0].StartsAt, null, Now);
            _db.Seasons.Add(season);
            seasons.Insert(0, season);
            changed = true;
        }

        if (changed) await _db.SaveChangesAsync(ct);

        // Virada automática: só enquanto nenhum admin fechou temporada.
        while (!IsManual(seasons) && seasons[^1].IsOpen && seasons[^1].Year < currentYear)
        {
            var open = seasons[^1];
            var nextStart = StartOfYear(open.Year + 1);
            open.Close(nextStart, null, Now);
            await _db.SaveChangesAsync(ct);
            var next = new SeasonEntity(groupId, open.Year + 1, 1, nextStart);
            _db.Seasons.Add(next);
            await _db.SaveChangesAsync(ct);
            seasons.Add(next);
        }

        return seasons;
    }

    // ── Regras ───────────────────────────────────────────────────────────────

    private DateTime ValidateCut(SeasonEntity open, DateTimeOffset? cutAt)
    {
        var cut = cutAt?.UtcDateTime ?? Now;
        if (cut <= open.StartsAt)
            throw new InvalidOperationException("O corte tem de ser depois do inicio da temporada atual.");
        if (cut > Now + MaxFutureCut)
            throw new InvalidOperationException("O corte pode ser agendado para no maximo 31 dias a frente.");
        return cut;
    }

    /// <summary>A próxima pertence ao ano em que começa e ganha o próximo número desse ano.</summary>
    private static (int Year, int Number) NextSlot(IReadOnlyList<SeasonEntity> seasons, DateTime cut)
    {
        var year = YearOf(cut);
        var number = seasons.Where(s => s.Year == year).Select(s => s.Number).DefaultIfEmpty(0).Max() + 1;
        return (year, number);
    }

    private static bool IsManual(IEnumerable<SeasonEntity> seasons) => seasons.Any(s => s.ClosedByUserId.HasValue);

    /// <summary>A temporada que o "desfazer" reabre: a anterior à aberta, se foi fechada pelo admin.</summary>
    private static SeasonEntity? UndoTarget(IReadOnlyList<SeasonEntity> seasons)
    {
        var open = seasons.FirstOrDefault(s => s.IsOpen);
        var previous = open is null ? null : seasons.FirstOrDefault(s => s.EndsAt == open.StartsAt);
        return previous?.ClosedByUserId is null ? null : previous;
    }

    private static SeasonEntity OpenSeason(IEnumerable<SeasonEntity> seasons) =>
        seasons.FirstOrDefault(s => s.IsOpen) ?? throw new InvalidOperationException("Nao ha temporada aberta.");

    private static SeasonEntity Find(IEnumerable<SeasonEntity> seasons, Guid seasonId) =>
        seasons.FirstOrDefault(s => s.Id == seasonId) ?? throw new InvalidOperationException("Temporada nao encontrada.");

    private static string DisplayName(SeasonEntity season, IReadOnlyList<SeasonEntity> seasons) =>
        season.DisplayName(seasons.Count(s => s.Year == season.Year));

    private SeasonEntity? Current(IReadOnlyList<SeasonEntity> seasons)
    {
        var now = Now;
        return seasons.FirstOrDefault(s => s.Contains(now)) ?? seasons.LastOrDefault();
    }

    private static SeasonDto ToDto(SeasonEntity s, IReadOnlyList<SeasonEntity> seasons, SeasonEntity? current,
        IReadOnlyCollection<DateTime> matches, IReadOnlyCollection<DateTime> championships,
        IReadOnlyDictionary<Guid, string> closers) =>
        new(s.Id, s.Year, s.Number, s.Name, DisplayName(s, seasons),
            s.StartsAt, s.EndsAt, s.IsPreSeason, s.IsOpen, s.Id == current?.Id,
            matches.Count(s.Contains), championships.Count(s.Contains),
            s.ClosedAt, s.ClosedByUserId is { } by ? closers.GetValueOrDefault(by) : null);

    // ── Datas ────────────────────────────────────────────────────────────────

    /// <summary>Datas dos jogos da patota: partidas (sem as de campeonato) e campeonatos.</summary>
    private async Task<(List<DateTime> Matches, List<DateTime> Championships)> LoadPlayedAtAsync(
        Guid groupId, CancellationToken ct, bool finalizedOnly = true)
    {
        var matches = await _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == groupId && (!finalizedOnly || m.Status == MatchStatus.Finalized))
            .Select(m => m.PlayedAt).ToListAsync(ct);
        var championships = await _db.Championships.AsNoTracking()
            .Where(c => c.GroupId == groupId && (finalizedOnly
                ? c.Status == ChampionshipStatus.Finalized
                : c.Status != ChampionshipStatus.Cancelled))
            .Select(c => c.PlayedAt).ToListAsync(ct);
        return (matches, championships);
    }

    private static int YearOf(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), BrazilTz).Year;

    private static DateTime StartOfYear(int year) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), BrazilTz);

    private static TimeZoneInfo ResolveBrazilTz()
    {
        foreach (var id in new[] { "E. South America Standard Time", "America/Sao_Paulo" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília Time", "BRT");
    }

    // ── Execução ─────────────────────────────────────────────────────────────

    /// <summary>Ação do admin numa transação: as trocas de temporada aberta salvam em dois passos.</summary>
    private async Task<Result> Mutate(Guid groupId, CancellationToken ct, Func<List<SeasonEntity>, Task> action)
    {
        if (!await _db.Groups.AnyAsync(g => g.Id == groupId, ct))
            return Result.Fail("Patota nao encontrada.", ResultStatus.NotFound);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var seasons = await EnsureSeasonsAsync(groupId, ct);
            await action(seasons);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result.Ok();
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return Result.Fail(ex.Message);
        }
    }

    private static async Task<Result<T>> Run<T>(Func<Task<Result<T>>> action)
    {
        try { return await action(); }
        catch (InvalidOperationException ex) { return Result<T>.Fail(ex.Message); }
    }
}
