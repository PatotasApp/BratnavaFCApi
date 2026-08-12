using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Conquistas;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class ConquistaService : IConquistaService
{
    private readonly AppDbContext _db;
    private readonly IConquistaProjectionService _projection;
    private static readonly TimeZoneInfo BrazilTz = ResolveBrazilTz();

    private static readonly (int t, string nome)[] MarcoPresenca =
    {
        (1, "Estreante"), (10, "Da Casa"), (25, "Assíduo"), (100, "Centurião"),
        (200, "Bicentenário"), (500, "Lenda Viva"), (1000, "Imortal"),
    };
    private static readonly (int t, string nome)[] MarcoGols =
    {
        (1, "Primeiro Gol"), (5, "Pé Quente"), (10, "Finalizador"), (25, "Matador"),
        (50, "Goleador"), (100, "Artilheiro Nato"), (250, "Lenda do Gol"), (500, "Máquina de Gols"),
    };
    private static readonly (int t, string nome)[] MarcoAssist =
    {
        (1, "Primeira Assistência"), (5, "Bom de Passe"), (10, "Criador"), (25, "Garçom"),
        (50, "Maestro"), (100, "Cérebro"), (250, "Rei das Assistências"),
    };
    private static readonly (int t, string nome)[] MarcoMvp =
    {
        (1, "Craque da Partida"), (5, "Decisivo"), (15, "Fora de Série"), (30, "Referência"), (50, "Ídolo"),
    };

    public ConquistaService(AppDbContext db, IConquistaProjectionService projection)
    {
        _db = db;
        _projection = projection;
    }

    public async Task<GroupConquistasDto> GetGroupConquistasAsync(Guid groupId, CancellationToken ct = default)
    {
        await _projection.EnsureGroupProjectedAsync(groupId, ct);
        var season = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, BrazilTz).Year;
        var players = await _db.Players.AsNoTracking()
            .Where(p => p.GroupId == groupId && !p.IsGuest)
            .Select(p => new PlayerRef(p.Id, p.UserId, p.Name, p.IsGoalkeeper))
            .ToListAsync(ct);
        var projections = await _db.PlayerStatProjections.AsNoTracking()
            .Where(x => x.GroupId == groupId)
            .ToListAsync(ct);
        var titles = await _db.SeasonTitles.AsNoTracking()
            .Where(x => x.GroupId == groupId)
            .OrderByDescending(x => x.Season).ThenBy(x => x.Position)
            .ToListAsync(ct);

        var lifetime = projections.Where(x => x.Season == 0).ToDictionary(x => x.PlayerId);
        var current = projections.Where(x => x.Season == season).ToDictionary(x => x.PlayerId);
        var participatingPlayers = players.Where(p => lifetime.ContainsKey(p.Id)).ToList();
        var sampleSize = participatingPlayers.Count;

        var result = new GroupConquistasDto { GroupId = groupId, Season = season };
        foreach (var player in players)
        {
            lifetime.TryGetValue(player.Id, out var stats);
            stats ??= Empty(groupId, player.Id);
            var dto = new PlayerConquistasDto
            {
                PlayerId = player.Id,
                UserId = player.UserId,
                PlayerName = player.Name,
                IsGoalkeeper = player.IsGoalkeeper,
            };

            dto.Marcos.Add(Marco("presenca", "Presença", "🎽", MarcoPresenca, stats.Games,
                participatingPlayers.Select(p => lifetime[p.Id].Games).ToArray(), sampleSize));
            dto.Marcos.Add(Marco("gols", "Gols", "⚽", MarcoGols, stats.Goals,
                participatingPlayers.Select(p => lifetime[p.Id].Goals).ToArray(), sampleSize));
            dto.Marcos.Add(Marco("assist", "Assistências", "🅰️", MarcoAssist, stats.Assists,
                participatingPlayers.Select(p => lifetime[p.Id].Assists).ToArray(), sampleSize));
            dto.Marcos.Add(Marco("mvp", "MVPs", "🏅", MarcoMvp, stats.Mvps,
                participatingPlayers.Select(p => lifetime[p.Id].Mvps).ToArray(), sampleSize));

            AddEvents(dto, stats, lifetime.Values.ToList(), sampleSize, player.IsGoalkeeper);
            dto.Temporada = BuildSeasonStandings(player.Id, players, current);
            dto.Titulos = titles.Where(x => x.PlayerId == player.Id).Select(MapTitle).ToList();
            result.Players.Add(dto);
        }
        return result;
    }

    private static void AddEvents(PlayerConquistasDto dto, PlayerStatProjectionEntity s,
        IReadOnlyCollection<PlayerStatProjectionEntity> all, int sampleSize, bool isGoalkeeper)
    {
        dto.Eventos.Add(Evento("hat-trick", "Hat-trick", "Marcou 3 gols em uma partida", "Ataque", "🎩", s.HatTricks, all, x => x.HatTricks, sampleSize, "Rara"));
        dto.Eventos.Add(Evento("poker", "Poker", "Marcou 4 gols em uma partida", "Ataque", "🃏", s.Pokers, all, x => x.Pokers, sampleSize, "Epica"));
        dto.Eventos.Add(Evento("cinco-estrelas", "Cinco estrelas", "Marcou 5 ou mais gols em uma partida", "Ataque", "⭐", s.FiveGoalGames, all, x => x.FiveGoalGames, sampleSize, "Lendaria"));
        dto.Eventos.Add(Evento("completo", "Jogo completo", "Marcou e deu assistência na mesma partida", "Criacao", "✨", s.GoalAndAssistGames, all, x => x.GoalAndAssistGames, sampleSize, "Comum"));
        dto.Eventos.Add(Evento("garcom-gala", "Garçom de gala", "Deu 3 ou mais assistências em uma partida", "Criacao", "🍽️", s.ThreeAssistGames, all, x => x.ThreeAssistGames, sampleSize, "Rara"));
        dto.Eventos.Add(Evento("invicto", "Invicto", "Completou 5 jogos seguidos sem perder", "Vitorias", "🛡️", s.UnbeatenFiveRuns, all, x => x.UnbeatenFiveRuns, sampleSize, "Rara"));
        dto.Eventos.Add(Evento("super-invicto", "Super-invicto", "Completou 10 jogos seguidos sem perder", "Vitorias", "🏰", s.UnbeatenTenRuns, all, x => x.UnbeatenTenRuns, sampleSize, "Epica"));
        dto.Eventos.Add(Evento("embalado", "Embalado", "Completou 5 vitórias seguidas", "Vitorias", "🔥", s.FiveWinRuns, all, x => x.FiveWinRuns, sampleSize, "Rara"));
        if (isGoalkeeper)
        {
            dto.Eventos.Add(Evento("muralha", "Muralha", "Terminou uma partida sem sofrer gols", "Goleiro", "🧤", s.CleanSheets, all, x => x.CleanSheets, sampleSize, "Comum"));
            dto.Eventos.Add(Evento("fortaleza", "Fortaleza", "Completou 3 jogos seguidos sem sofrer gols", "Goleiro", "🏯", s.ThreeCleanSheetRuns, all, x => x.ThreeCleanSheetRuns, sampleSize, "Rara"));
        }
        dto.Eventos.Add(Evento("contra", "Contra!", "Marcou um gol contra", "Zoeira", "🙈", s.OwnGoals, all, x => x.OwnGoals, sampleSize, "Comum"));
    }

    private static List<SeasonStandingDto> BuildSeasonStandings(Guid playerId,
        IReadOnlyCollection<PlayerRef> players, IReadOnlyDictionary<Guid, PlayerStatProjectionEntity> current)
    {
        var result = new List<SeasonStandingDto>();
        Rank("Gols", "Gols na temporada", "⚽", x => x.Goals, x => true);
        Rank("Assistencias", "Assistências na temporada", "🅰️", x => x.Assists, x => true);
        Rank("MVPs", "MVPs na temporada", "🏅", x => x.Mvps, x => true);
        Rank("Presenca", "Presença na temporada", "🎽", x => x.Games, x => true);
        Rank("Aproveitamento", "Aproveitamento na temporada", "📈", x => x.WinRatePct, x => x.Games >= 5);
        if (players.FirstOrDefault(x => x.Id == playerId)?.IsGoalkeeper == true)
            Rank("CleanSheets", "Jogos sem sofrer gols", "🧤", x => x.CleanSheets,
                x => players.FirstOrDefault(p => p.Id == x.PlayerId)?.IsGoalkeeper == true);
        return result;

        void Rank(string category, string name, string icon,
            Func<PlayerStatProjectionEntity, int> value, Func<PlayerStatProjectionEntity, bool> eligible)
        {
            var eligibleRows = current.Values.Where(x => eligible(x) && value(x) > 0).ToList();
            var ranked = eligibleRows
                .GroupBy(value).OrderByDescending(x => x.Key).ToList();
            var position = 0;
            var seen = 0;
            foreach (var group in ranked)
            {
                position = seen + 1;
                foreach (var row in group)
                {
                    seen++;
                    if (row.PlayerId != playerId) continue;
                    var percentile = (double)position / Math.Max(eligibleRows.Count, 1);
                    result.Add(new SeasonStandingDto
                    {
                        Categoria = category, Nome = name, Icone = icon, Valor = value(row),
                        Posicao = position, Total = eligibleRows.Count, Percentil = percentile,
                        Raridade = RaridadePercentil(percentile, eligibleRows.Count),
                    });
                }
            }
        }
    }

    private static ConquistaDto Marco(string id, string category, string icon,
        (int t, string nome)[] track, int value, int[] groupValues, int sampleSize)
    {
        var level = -1;
        for (var i = 0; i < track.Length; i++) if (value >= track[i].t) level = i;
        var current = level >= 0 ? track[level] : track[0];
        var threshold = level >= 0 ? current.t : track[0].t;
        var fraction = level >= 0 && sampleSize > 0
            ? (double)groupValues.Count(v => v >= threshold) / sampleSize : 0;
        var fixedRarity = FixedMilestoneRarity(level, track.Length);
        return new ConquistaDto
        {
            Id = $"marco-{id}", Nome = level >= 0 ? current.nome : $"{category} (bloqueada)",
            Descricao = level >= 0 ? $"{category}: {value}" : $"Chegue a {track[0].t} para desbloquear",
            Categoria = category, Icone = icon, Desbloqueada = level >= 0, Valor = value,
            Meta = level + 1 < track.Length ? track[level + 1].t : null,
            Nivel = level + 1, TotalNiveis = track.Length,
            ProximoNome = level + 1 < track.Length ? track[level + 1].nome : null,
            PctPatota = fraction,
            Raridade = level >= 0 ? HybridRarity(fixedRarity, fraction, sampleSize) : "Comum",
        };
    }

    private static ConquistaDto Evento(string id, string name, string description,
        string category, string icon, int count, IReadOnlyCollection<PlayerStatProjectionEntity> all,
        Func<PlayerStatProjectionEntity, int> get, int sampleSize, string fixedRarity)
    {
        var fraction = sampleSize == 0 ? 0 : (double)all.Count(x => get(x) > 0) / sampleSize;
        return new ConquistaDto
        {
            Id = $"evento-{id}", Nome = name, Descricao = description, Categoria = category,
            Icone = icon, Desbloqueada = count > 0, Count = count, PctPatota = fraction,
            Raridade = count > 0 ? HybridRarity(fixedRarity, fraction, sampleSize) : "Comum",
        };
    }

    private static ConquistaDto MapTitle(SeasonTitleEntity title) => new()
    {
        Id = $"titulo-{title.Category.ToLowerInvariant()}-{title.Season}",
        Nome = $"{title.Name} da Temporada {title.Season}",
        Descricao = title.Position == 1 ? $"Campeão em {title.Season}" : $"{title.Position}º lugar em {title.Season}",
        Categoria = title.Category, Icone = title.Icon, Desbloqueada = true,
        Ano = title.Season, Posicao = title.Position, Valor = title.Value,
        Raridade = title.Position == 1 ? "Lendaria" : "Epica",
    };

    public static List<ConquistaDto> BuildCareerMarcos(int games, int goals, int assists, int mvps) => new()
    {
        Marco("presenca", "Presença", "🎽", MarcoPresenca, games, new[] { games }, 1),
        Marco("gols", "Gols", "⚽", MarcoGols, goals, new[] { goals }, 1),
        Marco("assist", "Assistências", "🅰️", MarcoAssist, assists, new[] { assists }, 1),
        Marco("mvp", "MVPs", "🏅", MarcoMvp, mvps, new[] { mvps }, 1),
    };

    private static string FixedMilestoneRarity(int level, int total) =>
        level < 0 ? "Comum" : level == total - 1 ? "Lendaria" :
        level >= Math.Max(4, total - 2) ? "Epica" : level >= 2 ? "Rara" : "Comum";

    private static string HybridRarity(string fixedRarity, double fraction, int sampleSize)
    {
        if (sampleSize < 8) return fixedRarity;
        var relative = fraction <= .05 ? "Lendaria" : fraction <= .15 ? "Epica" : fraction <= .35 ? "Rara" : "Comum";
        return RarityRank(relative) < RarityRank(fixedRarity) ? relative : fixedRarity;
    }

    private static string RaridadePercentil(double percentile, int sampleSize) => sampleSize < 8
        ? (percentile <= .25 ? "Rara" : "Comum")
        : percentile <= .05 ? "Lendaria" : percentile <= .15 ? "Epica" : percentile <= .35 ? "Rara" : "Comum";

    private static int RarityRank(string value) => value switch
    {
        "Lendaria" => 4, "Epica" => 3, "Rara" => 2, _ => 1,
    };

    private static PlayerStatProjectionEntity Empty(Guid groupId, Guid playerId) =>
        new(groupId, playerId, 0, Array.Empty<PlayerMatchStatContributionEntity>());

    private sealed record PlayerRef(Guid Id, Guid? UserId, string Name, bool IsGoalkeeper);

    private static TimeZoneInfo ResolveBrazilTz()
    {
        foreach (var id in new[] { "E. South America Standard Time", "America/Sao_Paulo" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília Time", "BRT");
    }
}
