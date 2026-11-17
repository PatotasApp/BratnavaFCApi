using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Telão do Puskas (só admin): os replays de gol mais curtidos, cada um com o autor do gol
/// informado pelo admin. A lista e a colocação (empatados entram) vêm do ranking de curtidos.
/// </summary>
public sealed class PuskasService : IPuskasService
{
    private readonly AppDbContext _db;
    private readonly IReplayRankingService _ranking;

    public PuskasService(AppDbContext db, IReplayRankingService ranking)
    {
        _db = db;
        _ranking = ranking;
    }

    public async Task<Result<PuskasDto>> GetAsync(Guid groupId, int top, DateTime? from, DateTime? to, CancellationToken ct)
    {
        top = Math.Clamp(top, 1, 50);
        var ranked = await _ranking.GetRankingAsync(groupId, goalsOnly: true, from, to, minLikes: 1, top, ct);
        if (!ranked.Success || ranked.Data is null)
            return Result<PuskasDto>.Fail(ranked.Error ?? "Falha ao carregar os replays.", ranked.Status);

        var items = ranked.Data.Items;
        var clipIds = items.Select(i => i.Id).ToList();
        var matchIds = items.Select(i => i.MatchId).Distinct().ToList();

        var players = await _db.Players.AsNoTracking()
            .Where(p => p.GroupId == groupId)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(ct);
        var everyone = players.Select(p => p.Name).ToList();
        var nameById = players.ToDictionary(p => p.Id, p => p.Name);

        var scorers = await _db.ReplayClipScorers.AsNoTracking()
            .Where(s => s.GroupId == groupId && clipIds.Contains(s.ClipId))
            .ToDictionaryAsync(s => s.ClipId, s => s.PlayerId, ct);

        var lineups = await _db.MatchPlayers.AsNoTracking()
            .Where(mp => matchIds.Contains(mp.MatchId) && (mp.Team == 1 || mp.Team == 2) && !mp.DidNotPlay)
            .Select(mp => new { mp.MatchId, mp.PlayerId, mp.Team })
            .ToListAsync(ct);
        var lineupByMatch = lineups.GroupBy(l => l.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        var result = items.Select(item =>
        {
            var candidates = lineupByMatch.TryGetValue(item.MatchId, out var lineup)
                ? lineup.Where(l => nameById.ContainsKey(l.PlayerId))
                        .Select(l => new PuskasCandidateDto(l.PlayerId, nameById[l.PlayerId], l.Team))
                        .OrderBy(c => c.Team).ThenBy(c => c.Name).ToList()
                // Partida sem escalação por times (ex.: jogo de campeonato): qualquer jogador da patota.
                : players.OrderBy(p => p.Name).Select(p => new PuskasCandidateDto(p.Id, p.Name, 0)).ToList();

            Guid? scorerId = scorers.TryGetValue(item.Id, out var sid) ? sid : null;
            string? scorerName = scorerId is { } id && nameById.TryGetValue(id, out var full)
                ? ReplayRankingService.ShortName(full, everyone)
                : null;
            return new PuskasClipDto(item, scorerId, scorerName, candidates);
        }).ToList();

        return Result<PuskasDto>.Ok(new PuskasDto(
            ranked.Data.GroupName, top, result.Count(r => r.ScorerPlayerId is not null), result));
    }

    public async Task<Result> SetScorerAsync(Guid groupId, Guid clipId, Guid? playerId, CancellationToken ct)
    {
        var clipExists = await _db.ReplayClips.AsNoTracking().AnyAsync(c => c.Id == clipId && c.GroupId == groupId, ct);
        if (!clipExists) return Result.Fail("Replay não encontrado.", ResultStatus.NotFound);

        var current = await _db.ReplayClipScorers.FirstOrDefaultAsync(s => s.ClipId == clipId && s.GroupId == groupId, ct);

        if (playerId is null)
        {
            if (current is not null)
            {
                _db.ReplayClipScorers.Remove(current);
                await _db.SaveChangesAsync(ct);
            }
            return Result.Ok();
        }

        var playerExists = await _db.Players.AsNoTracking().AnyAsync(p => p.Id == playerId.Value && p.GroupId == groupId, ct);
        if (!playerExists) return Result.Fail("Jogador não encontrado nesta patota.", ResultStatus.NotFound);

        if (current is null) _db.ReplayClipScorers.Add(new ReplayClipScorerEntity(groupId, clipId, playerId.Value));
        else if (current.PlayerId != playerId.Value) current.Reassign(playerId.Value);
        else return Result.Ok();

        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
