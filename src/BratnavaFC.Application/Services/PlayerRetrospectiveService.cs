using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Statistics;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Retrospective;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Retrospectiva individual (estilo Wrapped). Usa a mesma fonte das Estatísticas: partidas
/// finalizadas + campeonatos pelas regras de cada um, e o jogo entra no período pela data.
/// </summary>
public sealed class PlayerRetrospectiveService : IPlayerRetrospectiveService
{
    private static readonly TimeZoneInfo BrazilTz = ResolveBrazilTz();

    private readonly AppDbContext _db;
    private readonly Func<DateTime> _utcNow;

    public PlayerRetrospectiveService(AppDbContext db) : this(db, () => DateTime.UtcNow) { }

    internal PlayerRetrospectiveService(AppDbContext db, Func<DateTime> utcNow)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _utcNow = utcNow;
    }

    public async Task<Result<PlayerRetrospectiveDto>> GetAsync(
        Guid groupId, Guid playerId, int? year, DateOnly? from, DateOnly? to, bool withPlayerList,
        CancellationToken ct = default)
    {
        var group = await _db.Groups.AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new { g.Name })
            .FirstOrDefaultAsync(ct);
        if (group is null)
            return Result<PlayerRetrospectiveDto>.Fail("Patota não encontrada.", ResultStatus.NotFound);

        var players = await _db.Players.AsNoTracking()
            .Where(p => p.GroupId == groupId)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(ct);
        var player = players.FirstOrDefault(p => p.Id == playerId);
        if (player is null)
            return Result<PlayerRetrospectiveDto>.Fail("Jogador não encontrado nesta patota.", ResultStatus.NotFound);

        if ((from is null) != (to is null))
            return Result<PlayerRetrospectiveDto>.Fail("Informe as duas datas do período.", ResultStatus.BadRequest);
        if (from > to)
            return Result<PlayerRetrospectiveDto>.Fail("A data inicial é depois da final.", ResultStatus.BadRequest);

        // Anos com jogo (Brasília), para o seletor.
        var matchDates = await _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.Finalized)
            .Select(m => m.PlayedAt)
            .ToListAsync(ct);
        var championshipUnits = await new ChampionshipStatsSource(_db).GetUnitsAsync(groupId, ct);
        var availableYears = matchDates.Concat(championshipUnits.Select(u => u.Stat.PlayedAt))
            .Select(d => ToLocal(d).Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        var nowUtc = _utcNow();
        RetrospectivePeriodDto period;
        if (from is DateOnly f && to is DateOnly t)
        {
            period = new RetrospectivePeriodDto
            {
                Start = ToUtc(f),
                End = ToUtc(t.AddDays(1)),
                Label = $"{f:dd/MM/yyyy} a {t:dd/MM/yyyy}",
            };
        }
        else
        {
            var currentYear = ToLocal(nowUtc).Year;
            var y = year ?? (availableYears.Contains(currentYear) || availableYears.Count == 0 ? currentYear : availableYears[0]);
            period = new RetrospectivePeriodDto
            {
                Year = y,
                Start = ToUtc(new DateOnly(y, 1, 1)),
                End = ToUtc(new DateOnly(y + 1, 1, 1)),
                Label = y.ToString(),
            };
        }
        period.IsPartial = period.End > nowUtc;

        var start = period.Start;
        var end = period.End;
        var matches = await _db.Matches.AsNoTracking().AsSplitQuery()
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.Finalized && m.PlayedAt >= start && m.PlayedAt < end)
            .Include(m => m.Players)
            .Include(m => m.Goals)
            .Include(m => m.Votes)
            .ToListAsync(ct);

        var range = new StatPeriod([new StatPeriodRange(start, end)], period.Label);
        var units = matches.Select(StatMatch.FromMatch)
            .Concat(championshipUnits.Select(u => u.Stat))
            .Where(u => range.Contains(u.PlayedAt))
            .ToList();

        var names = ShortNames.Build(players.Select(p => (p.Id, p.Name)));
        var dto = PlayerRetrospectiveCalculator.Build(playerId, units, names, ToLocal);

        dto.GroupId = groupId;
        dto.GroupName = group.Name;
        dto.Period = period;
        dto.AvailableYears = availableYears;
        dto.Player = new RetrospectivePlayerOptionDto
        {
            PlayerId = player.Id,
            Name = player.Name,
            ShortName = names[player.Id],
            Games = dto.Presence.Games,
        };

        if (withPlayerList)
        {
            var games = units
                .SelectMany(u => u.AllPlayers.Select(p => p.PlayerId).Distinct())
                .GroupBy(id => id)
                .ToDictionary(g => g.Key, g => g.Count());
            dto.Players = players
                .Where(p => games.ContainsKey(p.Id))
                .Select(p => new RetrospectivePlayerOptionDto { PlayerId = p.Id, Name = p.Name, ShortName = names[p.Id], Games = games[p.Id] })
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        return Result<PlayerRetrospectiveDto>.Ok(dto);
    }

    private static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc), BrazilTz);

    private static DateTime ToUtc(DateOnly localDate) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(localDate.ToDateTime(TimeOnly.MinValue), BrazilTz), DateTimeKind.Utc);

    private static TimeZoneInfo ResolveBrazilTz()
    {
        foreach (var id in new[] { "E. South America Standard Time", "America/Sao_Paulo" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília Time", "BRT");
    }
}
