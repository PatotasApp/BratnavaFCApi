using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Share;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Cloudflare;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Dados dos links compartilhados. Decide só O QUE sai; QUEM pode ver (integrante, login) é
/// decidido no controller com as mesmas regras das rotas internas (IsGroupMemberAsync).
/// </summary>
public sealed class PublicShareService : IPublicShareService
{
    private readonly AppDbContext _db;
    private readonly IImageStorageService _images;

    public PublicShareService(AppDbContext db, IImageStorageService images)
    {
        _db = db;
        _images = images;
    }

    public async Task<PublicGroupResponseDto?> GetGroupAsync(Guid groupId, bool isMember, CancellationToken ct = default)
    {
        var group = await _db.Groups.AsNoTracking()
            .Where(g => g.Id == groupId && g.Status == Status.Active)
            .Select(g => new { g.Id, g.Name, g.LogoKey, g.CreateDate })
            .FirstOrDefaultAsync(ct);
        if (group is null) return null;

        var settings = await _db.GroupSettings.AsNoTracking()
            .Where(s => s.GroupId == groupId)
            .Select(s => new { s.PublicPageEnabled, s.DefaultDayOfWeek, s.DefaultKickoffTime, s.DefaultPlaceName })
            .FirstOrDefaultAsync(ct);

        // Sem linha de configurações = padrão (ligada).
        if (settings is { PublicPageEnabled: false })
            return new PublicGroupResponseDto { Enabled = false, IsMember = isMember };

        var members = await _db.Players.AsNoTracking()
            .CountAsync(p => p.GroupId == groupId && !p.IsGuest && p.Status == Status.Active, ct);

        // O filtro global de Matches já deixa de fora os jogos de campeonato.
        var played = await _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.Finalized)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Goals = g.Sum(m => (m.TeamAGoals ?? 0) + (m.TeamBGoals ?? 0)) })
            .FirstOrDefaultAsync(ct);

        return new PublicGroupResponseDto
        {
            Enabled = true,
            IsMember = isMember,
            Group = new PublicGroupDto
            {
                GroupId = group.Id,
                Name = group.Name,
                LogoUrl = group.LogoKey is null ? null : _images.PublicBaseUrl + "/" + group.LogoKey,
                SinceYear = group.CreateDate.Year,
                MembersCount = members,
                MatchesPlayed = played?.Count ?? 0,
                GoalsScored = played?.Goals ?? 0,
                DayOfWeek = settings?.DefaultDayOfWeek is { } day ? (int)day : null,
                KickoffTime = settings?.DefaultKickoffTime is { } time ? time.ToString(@"hh\:mm") : null,
                PlaceName = string.IsNullOrWhiteSpace(settings?.DefaultPlaceName) ? null : settings!.DefaultPlaceName!.Trim(),
            },
        };
    }

    public async Task<Guid?> FindOwnerGroupAsync(string kind, Guid id, CancellationToken ct = default)
    {
        switch (kind.ToLowerInvariant())
        {
            case ShareKinds.Match:
            case ShareKinds.Mvp:
                return await _db.Matches.AsNoTracking()
                    .Where(m => m.Id == id).Select(m => (Guid?)m.GroupId).FirstOrDefaultAsync(ct);
            case ShareKinds.Championship:
                return await _db.Championships.AsNoTracking()
                    .Where(c => c.Id == id).Select(c => (Guid?)c.GroupId).FirstOrDefaultAsync(ct);
            case ShareKinds.Poll:
                return await _db.Polls.AsNoTracking()
                    .Where(p => p.Id == id).Select(p => (Guid?)p.GroupId).FirstOrDefaultAsync(ct);
            default:
                return null;
        }
    }

    public async Task<ShareTargetDto> BuildTargetAsync(string kind, Guid id, Guid groupId, string access, CancellationToken ct = default)
    {
        var normalizedKind = kind.ToLowerInvariant();
        var dto = new ShareTargetDto { Kind = normalizedKind, Id = id, Access = access };

        var group = await _db.Groups.AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new { g.Name, g.LogoKey, g.Status })
            .FirstOrDefaultAsync(ct);
        var publicPage = group is { Status: Status.Active } && !await _db.GroupSettings.AsNoTracking()
            .AnyAsync(s => s.GroupId == groupId && !s.PublicPageEnabled, ct);

        var isMember = access == ShareAccess.Member;
        if (isMember || publicPage)
        {
            dto.GroupId = groupId;
            dto.GroupName = group?.Name;
            dto.GroupLogoUrl = group?.LogoKey is null ? null : _images.PublicBaseUrl + "/" + group.LogoKey;
        }

        if (!isMember) return dto;

        switch (normalizedKind)
        {
            case ShareKinds.Match:
            case ShareKinds.Mvp:
                dto.MatchStatus = (await _db.Matches.AsNoTracking()
                    .Where(m => m.Id == id).Select(m => (MatchStatus?)m.Status).FirstOrDefaultAsync(ct))?.ToString();
                break;
            case ShareKinds.Poll:
                dto.PollType = await _db.Polls.AsNoTracking()
                    .Where(p => p.Id == id).Select(p => p.Type).FirstOrDefaultAsync(ct);
                break;
        }

        return dto;
    }
}
