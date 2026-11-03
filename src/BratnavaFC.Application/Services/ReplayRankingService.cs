using System.Text.RegularExpressions;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Cloudflare;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Ranking dos replays mais curtidos (aba Curtidos do admin e modo telão).
///
/// Lê jogos de campeonato só para mostrar placar e times do clipe (IgnoreQueryFilters
/// restrito aos MatchIds dos clipes); nada daqui entra em estatística.
/// </summary>
public sealed partial class ReplayRankingService : IReplayRankingService
{
    private static readonly MatchEventType[] GoalTypes =
        [MatchEventType.Gol, MatchEventType.GolTimeA, MatchEventType.GolTimeB];

    private readonly AppDbContext _db;
    private readonly IReplayUrlService _replayUrls;

    public ReplayRankingService(AppDbContext db, IReplayUrlService replayUrls)
    {
        _db = db;
        _replayUrls = replayUrls;
    }

    private sealed record MatchInfo(
        DateTime PlayedAt, string? TeamAName, string? TeamAHex, string? TeamBName, string? TeamBHex,
        int? TeamAGoals, int? TeamBGoals, Guid? ChampionshipId, string? ChampionshipName, int? GameOrder);

    public async Task<Result<ReplayRankingDto>> GetRankingAsync(
        Guid groupId, bool goalsOnly, DateTime? from, DateTime? to, int minLikes, int top, CancellationToken ct)
    {
        minLikes = Math.Max(1, minLikes);
        top = Math.Max(0, top);

        var clipsQuery = _db.ReplayClips.AsNoTracking().Where(c => c.GroupId == groupId);
        if (goalsOnly) clipsQuery = clipsQuery.Where(c => GoalTypes.Contains(c.EventType));
        var clips = await clipsQuery.ToListAsync(ct);
        var groupName = await _db.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => g.Name).FirstOrDefaultAsync(ct);
        if (clips.Count == 0) return Result<ReplayRankingDto>.Ok(new ReplayRankingDto(groupName, [], []));

        var clipIds = clips.Select(c => c.Id).ToList();
        var likes = await _db.ReplayLikes.AsNoTracking()
            .Where(l => clipIds.Contains(l.ClipId))
            .Select(l => new { l.ClipId, l.UserId, l.CreatedAt })
            .ToListAsync(ct);
        var likesByClip = likes.GroupBy(l => l.ClipId).ToDictionary(g => g.Key, g => g.OrderBy(l => l.CreatedAt).ToList());

        var liked = clips.Where(c => likesByClip.ContainsKey(c.Id)).ToList();
        var matches = await LoadMatchesAsync(groupId, liked.Select(c => c.MatchId).Distinct().ToList(), ct);
        DateTime? PlayedAt(Guid matchId) => matches.TryGetValue(matchId, out var m) ? m.PlayedAt : null;
        DateTime ClipDate(Domain.Entities.ReplayClipEntity c) => PlayedAt(c.MatchId) ?? c.RecordedAt.UtcDateTime;

        var years = liked.Select(c => ClipDate(c).Year).Distinct().OrderByDescending(y => y).ToList();

        var pool = liked
            .Where(c => likesByClip[c.Id].Count >= minLikes)
            .Where(c => (from is null || ClipDate(c) >= from.Value) && (to is null || ClipDate(c) < to.Value))
            .Select(c => new { Clip = c, Likes = likesByClip[c.Id].Count, Time = ParseClipTime(c.ObjectKey) })
            .OrderByDescending(x => x.Likes)
            .ThenByDescending(x => ClipDate(x.Clip).Date)
            .ThenBy(x => x.Time ?? x.Clip.RecordedAt.ToString("HH:mm:ss"))
            .ThenBy(x => x.Clip.Id)
            .ToList();

        var ranks = new int[pool.Count];
        for (var i = 0; i < pool.Count; i++)
            ranks[i] = i > 0 && pool[i].Likes == pool[i - 1].Likes ? ranks[i - 1] : i + 1;

        var cut = Enumerable.Range(0, pool.Count).Where(i => top == 0 || ranks[i] <= top).ToList();
        var names = await LoadLikerNamesAsync(groupId,
            cut.SelectMany(i => likesByClip[pool[i].Clip.Id].Select(l => l.UserId)).Distinct().ToList(), ct);

        var items = cut.Select(i =>
        {
            var c = pool[i].Clip;
            var m = matches.GetValueOrDefault(c.MatchId);
            return new RankedReplayDto(
                c.Id, c.MatchId, c.ObjectKey, _replayUrls.GeneratePresignedUrl(c.ObjectKey), c.EventType.ToString(),
                c.RecordedAt, pool[i].Time, m?.PlayedAt, ranks[i], pool[i].Likes,
                likesByClip[c.Id].Select(l => names.GetValueOrDefault(l.UserId, "?")).ToList(),
                m?.TeamAName, m?.TeamAHex, m?.TeamBName, m?.TeamBHex, m?.TeamAGoals, m?.TeamBGoals,
                m?.ChampionshipId, m?.ChampionshipName, m?.GameOrder);
        }).ToList();

        return Result<ReplayRankingDto>.Ok(new ReplayRankingDto(groupName, years, items));
    }

    /// <summary>Partidas comuns e jogos de campeonato dos clipes, com placar e times.</summary>
    private async Task<Dictionary<Guid, MatchInfo>> LoadMatchesAsync(Guid groupId, List<Guid> matchIds, CancellationToken ct)
    {
        var result = await _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == groupId && matchIds.Contains(m.Id))
            .Select(m => new
            {
                m.Id, m.PlayedAt, m.TeamAGoals, m.TeamBGoals,
                AName = m.TeamAColor != null ? m.TeamAColor.Name : null,
                AHex = m.TeamAColor != null ? m.TeamAColor.HexValue : null,
                BName = m.TeamBColor != null ? m.TeamBColor.Name : null,
                BHex = m.TeamBColor != null ? m.TeamBColor.HexValue : null,
            })
            .ToDictionaryAsync(m => m.Id,
                m => new MatchInfo(m.PlayedAt, m.AName, m.AHex, m.BName, m.BHex, m.TeamAGoals, m.TeamBGoals, null, null, null), ct);

        var missing = matchIds.Where(id => !result.ContainsKey(id)).ToList();
        if (missing.Count == 0) return result;

        var games = await _db.Matches.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.GroupId == groupId && m.ChampionshipId != null && missing.Contains(m.Id))
            .Select(m => new
            {
                m.Id, ChampionshipId = m.ChampionshipId!.Value, m.ChampionshipTeamAId, m.ChampionshipTeamBId,
                m.ChampionshipGameOrder, m.TeamAGoals, m.TeamBGoals,
            })
            .ToListAsync(ct);
        if (games.Count == 0) return result;

        var championshipIds = games.Select(g => g.ChampionshipId).Distinct().ToList();
        var championships = await _db.Championships.AsNoTracking()
            .Where(c => championshipIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.PlayedAt })
            .ToDictionaryAsync(c => c.Id, ct);
        var teams = await _db.ChampionshipTeams.AsNoTracking()
            .Where(t => championshipIds.Contains(t.ChampionshipId))
            .Select(t => new { t.Id, t.Name, Hex = t.Color != null ? t.Color.HexValue : null })
            .ToDictionaryAsync(t => t.Id, ct);

        foreach (var g in games)
        {
            var champ = championships.GetValueOrDefault(g.ChampionshipId);
            var a = g.ChampionshipTeamAId.HasValue ? teams.GetValueOrDefault(g.ChampionshipTeamAId.Value) : null;
            var b = g.ChampionshipTeamBId.HasValue ? teams.GetValueOrDefault(g.ChampionshipTeamBId.Value) : null;
            // A data que vale é a do campeonato (o jogo guarda o instante UTC em que foi criado).
            result[g.Id] = new MatchInfo(champ?.PlayedAt ?? default, a?.Name, a?.Hex, b?.Name, b?.Hex,
                g.TeamAGoals, g.TeamBGoals, g.ChampionshipId, champ?.Name, g.ChampionshipGameOrder);
        }
        return result;
    }

    /// <summary>
    /// Nome de quem curtiu: o do jogador da patota ligado ao usuário (senão o do perfil),
    /// encurtado para o primeiro nome quando ninguém mais da patota tem o mesmo primeiro nome.
    /// </summary>
    private async Task<Dictionary<Guid, string>> LoadLikerNamesAsync(Guid groupId, List<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return [];

        var players = await _db.Players.AsNoTracking()
            .Where(p => p.GroupId == groupId)
            .Select(p => new { p.Name, p.UserId })
            .ToListAsync(ct);
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
            .ToListAsync(ct);

        var full = users.ToDictionary(u => u.Id, u =>
            players.FirstOrDefault(p => p.UserId == u.Id)?.Name
            ?? NonEmpty($"{u.FirstName} {u.LastName}".Trim())
            ?? u.UserName);

        var everyone = players.Select(p => p.Name).Concat(full.Values).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return full.ToDictionary(kv => kv.Key, kv => ShortName(kv.Value, everyone));
    }

    private static string? NonEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string FirstName(string name) => name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;

    /// <summary>Primeiro nome, a não ser que outra pessoa da lista tenha o mesmo primeiro nome.</summary>
    internal static string ShortName(string name, IReadOnlyCollection<string> everyone)
    {
        var first = FirstName(name);
        var clash = everyone.Any(n => !string.Equals(n.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)
                                   && string.Equals(FirstName(n), first, StringComparison.OrdinalIgnoreCase));
        return clash ? name.Trim() : first;
    }

    /// <summary>
    /// Horário real do lance pelo nome do arquivo gerado pelo goal-replay ("…/211313_GolTimeA_FULL.mp4"
    /// → "21:13:13"). O RecordedAt de clipes importados é o horário da importação, não do lance.
    /// </summary>
    internal static string? ParseClipTime(string objectKey)
    {
        var file = objectKey[(objectKey.LastIndexOf('/') + 1)..];
        var m = ClipTimeRegex().Match(file);
        if (!m.Success) return null;
        int h = int.Parse(m.Groups[1].Value), min = int.Parse(m.Groups[2].Value), s = int.Parse(m.Groups[3].Value);
        return h < 24 && min < 60 && s < 60 ? $"{h:00}:{min:00}:{s:00}" : null;
    }

    [GeneratedRegex(@"^(\d{2})(\d{2})(\d{2})_")]
    private static partial Regex ClipTimeRegex();
}
