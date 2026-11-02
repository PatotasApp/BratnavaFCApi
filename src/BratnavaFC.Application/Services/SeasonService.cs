using BratnavaFC.Application.Abstractions;
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

    public SeasonService(AppDbContext db, TimeProvider? clock = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
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
        var (matches, championships) = await LoadPlayedAtAsync(groupId, ct);

        var closerIds = seasons.Where(s => s.ClosedByUserId.HasValue).Select(s => s.ClosedByUserId!.Value).Distinct().ToList();
        var closers = await _db.Users.AsNoTracking()
            .Where(u => closerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var now = Now;
        var current = seasons.FirstOrDefault(s => s.Contains(now)) ?? seasons.LastOrDefault();
        var dtos = seasons.Select(s => new SeasonDto(
                s.Id, s.Year, s.Number, s.Name, DisplayName(s, seasons),
                s.StartsAt, s.EndsAt, s.IsPreSeason, s.IsOpen, s.Id == current?.Id,
                matches.Count(s.Contains), championships.Count(s.Contains),
                s.ClosedAt, s.ClosedByUserId is { } by ? closers.GetValueOrDefault(by) : null))
            .ToList();

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

    public Task<Result> CloseAsync(Guid groupId, Guid? userId, CloseSeasonDto dto, CancellationToken ct) =>
        Mutate(groupId, ct, async seasons =>
        {
            var open = OpenSeason(seasons);
            var cut = ValidateCut(open, dto.CutAt);
            var (nextYear, nextNumber) = NextSlot(seasons, cut);
            var next = new SeasonEntity(groupId, nextYear, nextNumber, cut, dto.NextName, dto.NextIsPreSeason);

            // Fecha primeiro e abre depois: o índice só aceita uma aberta por patota.
            open.Close(cut, userId, Now);
            await _db.SaveChangesAsync(ct);
            _db.Seasons.Add(next);
        });

    public Task<Result> UpdateAsync(Guid groupId, Guid seasonId, UpdateSeasonDto dto, CancellationToken ct) =>
        Mutate(groupId, ct, seasons =>
        {
            var season = Find(seasons, seasonId);
            season.Rename(dto.Name);
            season.SetPreSeason(dto.IsPreSeason);
            return Task.CompletedTask;
        });

    public Task<Result> MoveStartAsync(Guid groupId, Guid seasonId, MoveSeasonStartDto dto, CancellationToken ct) =>
        Mutate(groupId, ct, seasons =>
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
            return Task.CompletedTask;
        });

    public Task<Result> UndoCloseAsync(Guid groupId, CancellationToken ct) =>
        Mutate(groupId, ct, async seasons =>
        {
            var previous = UndoTarget(seasons)
                ?? throw new InvalidOperationException("Nao ha fechamento feito pelo admin para desfazer.");
            var open = OpenSeason(seasons);

            // Apaga a aberta primeiro: o índice só aceita uma aberta por patota.
            _db.Seasons.Remove(open);
            await _db.SaveChangesAsync(ct);
            previous.Reopen();
        });

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
