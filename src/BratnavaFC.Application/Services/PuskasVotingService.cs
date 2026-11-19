using System.Globalization;
using System.Text;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Cloudflare;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Votação do Puskas: quem escaneia o QR code do telão escolhe até 3 gols, sem login, e o
/// admin apura. Um voto por nome (sem acento/maiúscula); a folha mostra só miniatura, autor e data.
/// </summary>
public sealed class PuskasVotingService : IPuskasVotingService
{
    public const int MaxVotes = 3;
    public const int TopClips = 10;
    private const int MaxNameLength = 60;

    private readonly AppDbContext _db;
    private readonly IPuskasService _puskas;
    private readonly IReplayUrlService _replayUrls;

    public PuskasVotingService(AppDbContext db, IPuskasService puskas, IReplayUrlService replayUrls)
    {
        _db = db;
        _puskas = puskas;
        _replayUrls = replayUrls;
    }

    private sealed record ClipInfo(Guid ClipId, string VideoUrl, string Scorer, DateTime? PlayedAt, string? Time, int Number);

    private async Task<PuskasPollDto> ToDtoAsync(PuskasPollEntity poll, CancellationToken ct)
    {
        var voters = await _db.PuskasVotes.AsNoTracking().Where(v => v.PollId == poll.Id)
            .Select(v => v.VoterKey).Distinct().CountAsync(ct);
        return new PuskasPollDto(poll.Code, poll.IsOpen, poll.ClipIds.Count, voters, MaxVotes);
    }

    public async Task<Result<PuskasPollDto?>> GetPollAsync(Guid groupId, CancellationToken ct)
    {
        var poll = await _db.PuskasPolls.AsNoTracking().FirstOrDefaultAsync(p => p.GroupId == groupId, ct);
        return Result<PuskasPollDto?>.Ok(poll is null ? null : await ToDtoAsync(poll, ct));
    }

    public async Task<Result<PuskasPollDto>> OpenPollAsync(Guid groupId, bool countdown, CancellationToken ct)
    {
        var current = await _puskas.GetAsync(groupId, TopClips, null, null, ct);
        if (!current.Success || current.Data is null)
            return Result<PuskasPollDto>.Fail(current.Error ?? "Falha ao carregar os gols.", current.Status);
        if (current.Data.Items.Count == 0)
            return Result<PuskasPollDto>.Fail("Nenhum gol curtido para votar.");
        // Só entram na votação os gols que já têm autor informado; o resto fica de fora.
        var clipIds = current.Data.Items.Where(i => i.ScorerPlayerId is not null).Select(i => i.Clip.Id).ToList();
        if (clipIds.Count == 0)
            return Result<PuskasPollDto>.Fail("Identifique o autor de pelo menos 1 gol antes de abrir a votação.");

        var poll = await _db.PuskasPolls.FirstOrDefaultAsync(p => p.GroupId == groupId, ct);
        if (poll is null)
        {
            poll = new PuskasPollEntity(groupId, clipIds, countdown);
            _db.PuskasPolls.Add(poll);
        }
        else
        {
            // Com votos já computados nenhum gol sai da lista (os votos apontam para ela);
            // gols recém-identificados podem ser acrescentados.
            var hasVotes = await _db.PuskasVotes.AnyAsync(v => v.PollId == poll.Id, ct);
            // Ordem do ranking atual; gols com voto que saíram do top ficam no fim.
            poll.SetClips(hasVotes ? clipIds.Concat(poll.ClipIds.Except(clipIds)) : clipIds, countdown);
            poll.SetOpen(true);
        }
        await _db.SaveChangesAsync(ct);
        return Result<PuskasPollDto>.Ok(await ToDtoAsync(poll, ct));
    }

    public async Task<Result<PuskasPollDto>> SetOpenAsync(Guid groupId, bool isOpen, CancellationToken ct)
    {
        var poll = await _db.PuskasPolls.FirstOrDefaultAsync(p => p.GroupId == groupId, ct);
        if (poll is null) return Result<PuskasPollDto>.Fail("Votação ainda não foi criada.", ResultStatus.NotFound);
        poll.SetOpen(isOpen);
        await _db.SaveChangesAsync(ct);
        return Result<PuskasPollDto>.Ok(await ToDtoAsync(poll, ct));
    }

    /// <summary>Gols da votação com autor e dados do lance, na ordem em que a lista foi montada (curtidas).</summary>
    private async Task<List<ClipInfo>> LoadClipsAsync(PuskasPollEntity poll, CancellationToken ct)
    {
        var ids = poll.ClipIds.ToList();
        var clips = await _db.ReplayClips.AsNoTracking().Where(c => c.GroupId == poll.GroupId && ids.Contains(c.Id)).ToListAsync(ct);
        var scorers = await _db.ReplayClipScorers.AsNoTracking().Where(s => s.GroupId == poll.GroupId && ids.Contains(s.ClipId)).ToListAsync(ct);
        var players = await _db.Players.AsNoTracking().Where(p => p.GroupId == poll.GroupId).Select(p => new { p.Id, p.Name }).ToListAsync(ct);
        var everyone = players.Select(p => p.Name).ToList();
        var nameById = players.ToDictionary(p => p.Id, p => p.Name);
        var matchIds = clips.Select(c => c.MatchId).Distinct().ToList();
        var playedAt = await _db.Matches.AsNoTracking().Where(m => matchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.PlayedAt }).ToDictionaryAsync(m => m.Id, m => (DateTime?)m.PlayedAt, ct);

        return clips
            .Select(c =>
            {
                var scorer = scorers.FirstOrDefault(s => s.ClipId == c.Id);
                var name = scorer is not null && nameById.TryGetValue(scorer.PlayerId, out var full)
                    ? ReplayRankingService.ShortName(full, everyone) : "?";
                return new ClipInfo(c.Id, _replayUrls.GeneratePresignedUrl(c.ObjectKey), name,
                    playedAt.GetValueOrDefault(c.MatchId), ReplayRankingService.ParseClipTime(c.ObjectKey), ids.IndexOf(c.Id) + 1);
            })
            .OrderBy(x => ids.IndexOf(x.ClipId))
            .ToList();
    }

    public async Task<Result<PuskasResultsDto>> GetResultsAsync(Guid groupId, CancellationToken ct)
    {
        var poll = await _db.PuskasPolls.AsNoTracking().FirstOrDefaultAsync(p => p.GroupId == groupId, ct);
        if (poll is null) return Result<PuskasResultsDto>.Fail("Votação ainda não foi criada.", ResultStatus.NotFound);

        var votes = await _db.PuskasVotes.AsNoTracking().Where(v => v.PollId == poll.Id).ToListAsync(ct);
        var clips = await LoadClipsAsync(poll, ct);

        var rows = clips
            .Select(c => (Clip: c, Voters: votes.Where(v => v.ClipId == c.ClipId).Select(v => v.VoterName).OrderBy(n => n).ToList()))
            .OrderByDescending(r => r.Voters.Count)
            .ThenBy(r => r.Clip.PlayedAt)
            .ToList();
        var items = new List<PuskasResultItemDto>();
        var rank = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (i == 0 || rows[i].Voters.Count != rows[i - 1].Voters.Count) rank = i + 1;
            var c = rows[i].Clip;
            items.Add(new PuskasResultItemDto(c.ClipId, c.VideoUrl, c.Scorer, c.PlayedAt, c.Time, rank, rows[i].Voters.Count, rows[i].Voters, c.Number));
        }

        var voterNames = votes.GroupBy(v => v.VoterKey).Select(g => g.First().VoterName).OrderBy(n => n).ToList();
        return Result<PuskasResultsDto>.Ok(new PuskasResultsDto(await ToDtoAsync(poll, ct), voterNames.Count, voterNames, items));
    }

    public async Task<Result<PuskasSheetDto>> GetSheetAsync(string code, CancellationToken ct)
    {
        var poll = await _db.PuskasPolls.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code, ct);
        if (poll is null) return Result<PuskasSheetDto>.Fail("Votação não encontrada.", ResultStatus.NotFound);

        var groupName = await _db.Groups.AsNoTracking().Where(g => g.Id == poll.GroupId).Select(g => g.Name).FirstOrDefaultAsync(ct);
        // Mesma ordem em que os vídeos passam no telão, com o número de cada um.
        var loaded = await LoadClipsAsync(poll, ct);
        var items = (poll.Countdown ? loaded.OrderByDescending(c => c.Number) : loaded.OrderBy(c => c.Number))
            .Select(c => new PuskasSheetItemDto(c.ClipId, c.VideoUrl, c.Scorer, c.PlayedAt, c.Time, c.Number))
            .ToList();
        return Result<PuskasSheetDto>.Ok(new PuskasSheetDto(groupName, poll.IsOpen, MaxVotes, items));
    }

    public async Task<Result> SubmitVoteAsync(string code, string voterName, IReadOnlyList<Guid> clipIds, CancellationToken ct)
    {
        var name = string.Join(' ', (voterName ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0) return Result.Fail("Digite seu nome.");
        if (name.Length > MaxNameLength) return Result.Fail($"O nome pode ter no máximo {MaxNameLength} letras.");

        var chosen = (clipIds ?? []).Distinct().ToList();
        if (chosen.Count == 0) return Result.Fail("Escolha pelo menos 1 gol.");
        if (chosen.Count > MaxVotes) return Result.Fail($"Escolha no máximo {MaxVotes} gols.");

        var poll = await _db.PuskasPolls.FirstOrDefaultAsync(p => p.Code == code, ct);
        if (poll is null) return Result.Fail("Votação não encontrada.", ResultStatus.NotFound);
        if (!poll.IsOpen) return Result.Fail("A votação está encerrada.", ResultStatus.Conflict);

        var valid = poll.ClipIds.ToHashSet();
        if (chosen.Any(c => !valid.Contains(c))) return Result.Fail("Gol inválido.");

        var key = Normalize(name);
        if (await _db.PuskasVotes.AnyAsync(v => v.PollId == poll.Id && v.VoterKey == key, ct))
            return Result.Fail("Esse nome já votou.", ResultStatus.Conflict);

        _db.PuskasVotes.AddRange(chosen.Select(c => new PuskasVoteEntity(poll.Id, c, name, key)));
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dois envios simultâneos com o mesmo nome: o índice único barra o segundo.
            return Result.Fail("Esse nome já votou.", ResultStatus.Conflict);
        }
        return Result.Ok("Obrigado!");
    }

    /// <summary>Minúsculo, sem acento e com espaços normalizados.</summary>
    internal static string Normalize(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
