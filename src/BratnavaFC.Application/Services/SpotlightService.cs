using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Statistics;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Domain.Models;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Spotlight do telão: lê a configuração da patota, recorta o período com os mesmos dados das
/// Estatísticas (partidas comuns + campeonatos pelas regras deles) e entrega tudo calculado.
/// </summary>
public sealed class SpotlightService : ISpotlightService
{
    private static readonly TimeZoneInfo BrazilTimeZone = ResolveBrazilTimeZone();

    private readonly AppDbContext _db;
    private readonly Func<DateTime> _utcNow;

    public SpotlightService(AppDbContext db) : this(db, () => DateTime.UtcNow) { }

    public SpotlightService(AppDbContext db, Func<DateTime> utcNow)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _utcNow = utcNow;
    }

    public async Task<Result<SpotlightSettings>> GetSettingsAsync(Guid groupId, CancellationToken ct = default)
    {
        if (!await _db.Groups.AnyAsync(g => g.Id == groupId, ct))
            return Result<SpotlightSettings>.Fail("Patota não encontrada.", ResultStatus.NotFound);

        var entity = await _db.GroupSettings.AsNoTracking().FirstOrDefaultAsync(x => x.GroupId == groupId, ct);
        return Result<SpotlightSettings>.Ok(entity?.GetSpotlightSettings() ?? new SpotlightSettings());
    }

    public async Task<Result<SpotlightSettings>> SaveSettingsAsync(Guid groupId, SpotlightSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!await _db.Groups.AnyAsync(g => g.Id == groupId, ct))
            return Result<SpotlightSettings>.Fail("Patota não encontrada.", ResultStatus.NotFound);

        var entity = await _db.GroupSettings.FirstOrDefaultAsync(x => x.GroupId == groupId, ct);
        if (entity is null)
        {
            // Mesmos padrões que o GET das configurações devolve quando nada foi salvo.
            entity = new GroupSettingsEntity(groupId, 5, 6, null, null, null);
            _db.GroupSettings.Add(entity);
        }

        try
        {
            entity.SetSpotlightSettings(settings);
        }
        catch (InvalidOperationException ex)
        {
            return Result<SpotlightSettings>.Fail(ex.Message, ResultStatus.BadRequest);
        }

        await _db.SaveChangesAsync(ct);
        return Result<SpotlightSettings>.Ok(entity.GetSpotlightSettings(), "Spotlight atualizado.");
    }

    public async Task<Result<SpotlightEventReport>> GetReportAsync(Guid groupId, CancellationToken ct = default)
    {
        var group = await _db.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => new { g.Id, g.Name }).FirstOrDefaultAsync(ct);
        if (group is null)
            return Result<SpotlightEventReport>.Fail("Patota não encontrada.", ResultStatus.NotFound);

        var groupSettings = await _db.GroupSettings.AsNoTracking().FirstOrDefaultAsync(x => x.GroupId == groupId, ct);
        var settings = groupSettings?.GetSpotlightSettings() ?? new SpotlightSettings();

        var (from, to) = ResolveDates(settings, _utcNow());

        var matchesQuery = _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.Finalized);
        if (from.HasValue) matchesQuery = matchesQuery.Where(m => m.PlayedAt >= from.Value);
        if (to.HasValue) matchesQuery = matchesQuery.Where(m => m.PlayedAt < to.Value);
        var matches = await matchesQuery
            .Include(m => m.Players)
            .Include(m => m.Goals)
            .Include(m => m.Votes)
            .AsSplitQuery()
            .ToListAsync(ct);

        var units = matches.Select(m => (Unit: new SpotlightUnit(StatMatch.FromMatch(m), IsSingleGame: true), ChampionshipId: (Guid?)null)).ToList();
        if (settings.IncludeChampionships)
        {
            var championshipUnits = await new ChampionshipStatsSource(_db).GetUnitsAsync(groupId, ct);
            units.AddRange(championshipUnits
                .Where(u => (!from.HasValue || u.Stat.PlayedAt >= from.Value) && (!to.HasValue || u.Stat.PlayedAt < to.Value))
                .Select(u => (new SpotlightUnit(u.Stat, IsSingleGame: u.Game is not null), (Guid?)u.Championship.Id)));
        }

        units = units.OrderBy(u => u.Unit.Stat.PlayedAt).ThenBy(u => u.Unit.Stat.Id).ToList();
        if (settings.PeriodMode == SpotlightPeriodMode.LastGames && settings.LastGames is int last)
            units = units.Skip(Math.Max(0, units.Count - last)).ToList();

        var regularIds = units.Where(u => u.ChampionshipId is null).Select(u => u.Unit.Stat.Id).ToList();
        var championshipIds = units.Where(u => u.ChampionshipId is not null).Select(u => u.ChampionshipId!.Value).Distinct().ToList();
        var replays = await _db.ReplayClips.AsNoTracking()
            .CountAsync(c => c.GroupId == groupId
                && ((c.ChampionshipId == null && regularIds.Contains(c.MatchId))
                    || (c.ChampionshipId != null && championshipIds.Contains(c.ChampionshipId.Value))), ct);

        var groupPlayers = await _db.Players.AsNoTracking()
            .Where(p => p.GroupId == groupId)
            .Select(p => new { p.Id, p.Name, p.IsGuest, p.IsGoalkeeper })
            .ToListAsync(ct);
        var names = DisplayNames(groupPlayers.Select(p => (p.Id, p.Name)));
        var players = groupPlayers.ToDictionary(
            p => p.Id,
            p => new SpotlightPlayerInfo(p.Id, names[p.Id], p.Name, p.IsGuest, p.IsGoalkeeper));
        var tracked = groupPlayers.Where(p => !settings.OnlyMonthly || !p.IsGuest).Select(p => p.Id).ToHashSet();

        var periodUnits = units.Select(u => u.Unit).ToList();
        var report = SpotlightCalculator.Build(periodUnits, players, tracked, replays, settings.MinGames);
        report.GroupId = group.Id;
        report.GroupName = group.Name;
        report.Settings = settings;
        report.Period = new SpotlightPeriodInfo
        {
            Mode = settings.PeriodMode,
            SeasonYear = settings.PeriodMode == SpotlightPeriodMode.Season ? settings.SeasonYear ?? LocalYear(_utcNow()) : null,
            SinceDate = settings.PeriodMode == SpotlightPeriodMode.Since ? settings.SinceDate : null,
            LastGames = settings.PeriodMode == SpotlightPeriodMode.LastGames ? settings.LastGames : null,
            OnlyMonthly = settings.OnlyMonthly,
            IncludeChampionships = settings.IncludeChampionships,
            FirstGameAt = periodUnits.FirstOrDefault()?.Stat.PlayedAt,
            LastGameAt = periodUnits.LastOrDefault()?.Stat.PlayedAt,
        };

        return Result<SpotlightEventReport>.Ok(report);
    }

    /// <summary>
    /// Limites do período em UTC: temporada = ano civil no horário de Brasília (como as
    /// conquistas); "desde" = meia-noite da data. Últimos N jogos e "tudo" não têm datas.
    /// </summary>
    public static (DateTime? From, DateTime? To) ResolveDates(SpotlightSettings settings, DateTime utcNow) => settings.PeriodMode switch
    {
        SpotlightPeriodMode.Season => (
            LocalMidnightToUtc(new DateOnly(settings.SeasonYear ?? LocalYear(utcNow), 1, 1)),
            LocalMidnightToUtc(new DateOnly((settings.SeasonYear ?? LocalYear(utcNow)) + 1, 1, 1))),
        SpotlightPeriodMode.Since when settings.SinceDate is DateOnly since => (LocalMidnightToUtc(since), null),
        _ => (null, null),
    };

    /// <summary>
    /// Primeiro nome, a não ser que outro jogador da patota (inclusive convidado ou inativo)
    /// tenha o mesmo primeiro nome: aí vai o nome completo.
    /// </summary>
    public static Dictionary<Guid, string> DisplayNames(IEnumerable<(Guid Id, string Name)> players)
    {
        var list = players.Select(p => (p.Id, Full: p.Name.Trim(), First: FirstName(p.Name))).ToList();
        var counts = list.GroupBy(p => p.First, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.CurrentCultureIgnoreCase);
        return list.ToDictionary(p => p.Id, p => counts[p.First] > 1 ? p.Full : p.First);
    }

    private static string FirstName(string name)
    {
        var trimmed = name.Trim();
        var space = trimmed.IndexOfAny([' ', '\t']);
        return space < 0 ? trimmed : trimmed[..space];
    }

    private static int LocalYear(DateTime utcNow) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), BrazilTimeZone).Year;

    private static DateTime LocalMidnightToUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), BrazilTimeZone);

    private static TimeZoneInfo ResolveBrazilTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time"); }
    }
}
