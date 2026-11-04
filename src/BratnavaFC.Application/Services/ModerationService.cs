using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Moderation;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class ModerationService : IModerationService
{
    private readonly AppDbContext _db;
    private readonly IPushService? _push;
    private readonly ILogger<ModerationService>? _logger;

    public ModerationService(AppDbContext db, IPushService? push = null, ILogger<ModerationService>? logger = null)
    {
        _db = db;
        _push = push;
        _logger = logger;
    }

    public async Task<Result> ReportAsync(Guid reporterUserId, CreateContentReportDto dto, CancellationToken ct)
    {
        if (!Enum.IsDefined(dto.TargetType)) return Result.Fail("Tipo de conteúdo inválido.");
        if (dto.TargetId == Guid.Empty) return Result.Fail("Informe o conteúdo denunciado.");
        if (string.IsNullOrWhiteSpace(dto.Reason)) return Result.Fail("Informe o motivo da denúncia.");

        // A mesma pessoa denunciando o mesmo conteúdo de novo não gera outra denúncia
        // nem outro aviso enquanto a primeira estiver pendente.
        var alreadyPending = await _db.ContentReports.AnyAsync(r =>
            r.ReporterUserId == reporterUserId && r.TargetType == dto.TargetType &&
            r.TargetId == dto.TargetId && !r.IsResolved, ct);
        if (alreadyPending) return Result.Ok("Denúncia recebida.");

        ContentReportEntity report;
        try
        {
            report = new ContentReportEntity(reporterUserId, dto.GroupId, dto.TargetType, dto.TargetId, dto.Reason, dto.Details);
        }
        catch (ArgumentException e)
        {
            return Result.Fail(e.Message);
        }

        _db.ContentReports.Add(report);
        await _db.SaveChangesAsync(ct);
        await NotifyModeratorsAsync(report, ct);
        return Result.Ok("Denúncia recebida. Vamos analisar em até 24 horas.");
    }

    /// <summary>
    /// Avisa quem pode agir: os admins da patota (quando a denúncia é de uma patota)
    /// e os administradores da plataforma.
    /// </summary>
    private async Task NotifyModeratorsAsync(ContentReportEntity report, CancellationToken ct)
    {
        if (_push is null) return;
        try
        {
            var data = new Dictionary<string, string>
            {
                ["type"] = "content_report",
                ["reportId"] = report.Id.ToString(),
            };
            var body = $"Motivo: {report.Reason}. Analise em até 24 horas.";

            var platformAdmins = await _db.Users
                .Where(u => u.Role == UserRole.Admin || u.Role == UserRole.GodMode)
                .Select(u => u.Id)
                .ToListAsync(ct);

            if (report.GroupId is { } groupId)
            {
                await _push.SendToGroupAdminsAsync(groupId, "Nova denúncia", body, data, ct);
                var groupAdmins = await _db.GroupAdmins.Where(a => a.GroupId == groupId).Select(a => a.UserId).ToListAsync(ct);
                platformAdmins = platformAdmins.Except(groupAdmins).ToList();
            }

            if (platformAdmins.Count > 0)
                await _push.SendToUsersAsync(platformAdmins, "Nova denúncia", body, data, ct);
        }
        catch (Exception ex)
        {
            // A denúncia já está salva; falha no aviso não pode devolver erro para quem denunciou.
            _logger?.LogError(ex, "[Moderação] Falha ao avisar sobre a denúncia {ReportId}.", report.Id);
        }
    }

    public async Task<Result<List<ContentReportDto>>> GetReportsAsync(bool pendingOnly, CancellationToken ct)
    {
        var query = _db.ContentReports.AsNoTracking();
        if (pendingOnly) query = query.Where(r => !r.IsResolved);

        var reports = await query.OrderByDescending(r => r.CreateDate).Take(200).ToListAsync(ct);
        var reporterIds = reports.Select(r => r.ReporterUserId).Distinct().ToList();
        var groupIds = reports.Where(r => r.GroupId.HasValue).Select(r => r.GroupId!.Value).Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => reporterIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), ct);
        var groups = await _db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        return Result<List<ContentReportDto>>.Ok(reports.Select(r => new ContentReportDto(
            r.Id, r.CreateDate, r.ReporterUserId, names.GetValueOrDefault(r.ReporterUserId) ?? "",
            r.GroupId, r.GroupId is { } g ? groups.GetValueOrDefault(g) : null,
            r.TargetType, r.TargetId, r.Reason, r.Details, r.IsResolved, r.ResolvedAt, r.ResolutionNote)).ToList());
    }

    public async Task<Result> ResolveReportAsync(Guid reportId, Guid resolvedByUserId, string? note, CancellationToken ct)
    {
        var report = await _db.ContentReports.FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null) return Result.Fail("Denúncia não encontrada.", ResultStatus.NotFound);
        report.Resolve(resolvedByUserId, note);
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> BlockAsync(Guid blockerUserId, Guid blockedUserId, CancellationToken ct)
    {
        if (blockerUserId == blockedUserId) return Result.Fail("Você não pode bloquear a si mesmo.");
        if (!await _db.Users.AnyAsync(u => u.Id == blockedUserId, ct))
            return Result.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        var exists = await _db.UserBlocks.AnyAsync(b => b.BlockerUserId == blockerUserId && b.BlockedUserId == blockedUserId, ct);
        if (!exists)
        {
            _db.UserBlocks.Add(new UserBlockEntity(blockerUserId, blockedUserId));
            await _db.SaveChangesAsync(ct);
        }
        return Result.Ok("Usuário bloqueado.");
    }

    public async Task<Result> UnblockAsync(Guid blockerUserId, Guid blockedUserId, CancellationToken ct)
    {
        var block = await _db.UserBlocks.FirstOrDefaultAsync(b => b.BlockerUserId == blockerUserId && b.BlockedUserId == blockedUserId, ct);
        if (block is not null)
        {
            _db.UserBlocks.Remove(block);
            await _db.SaveChangesAsync(ct);
        }
        return Result.Ok("Usuário desbloqueado.");
    }

    public async Task<Result<List<BlockedUserDto>>> GetBlockedAsync(Guid blockerUserId, CancellationToken ct)
    {
        var list = await _db.UserBlocks.AsNoTracking()
            .Where(b => b.BlockerUserId == blockerUserId)
            .Join(_db.Users.AsNoTracking(), b => b.BlockedUserId, u => u.Id,
                (b, u) => new BlockedUserDto(u.Id, (u.FirstName + " " + u.LastName).Trim(), b.CreateDate))
            .ToListAsync(ct);
        return Result<List<BlockedUserDto>>.Ok(list.OrderBy(x => x.Name).ToList());
    }

    public Task<bool> IsBlockedEitherWayAsync(Guid userA, Guid userB, CancellationToken ct) =>
        _db.UserBlocks.AnyAsync(b =>
            (b.BlockerUserId == userA && b.BlockedUserId == userB) ||
            (b.BlockerUserId == userB && b.BlockedUserId == userA), ct);
}
