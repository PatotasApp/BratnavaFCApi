using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Moderation;

namespace BratnavaFC.Application.Abstractions;

/// <summary>Denúncia de conteúdo e bloqueio de usuários (App Store, regra 1.2).</summary>
public interface IModerationService
{
    Task<Result> ReportAsync(Guid reporterUserId, CreateContentReportDto dto, CancellationToken ct);
    Task<Result<List<ContentReportDto>>> GetReportsAsync(bool pendingOnly, CancellationToken ct);
    Task<Result> ResolveReportAsync(Guid reportId, Guid resolvedByUserId, string? note, CancellationToken ct);

    Task<Result> BlockAsync(Guid blockerUserId, Guid blockedUserId, CancellationToken ct);
    Task<Result> UnblockAsync(Guid blockerUserId, Guid blockedUserId, CancellationToken ct);
    Task<Result<List<BlockedUserDto>>> GetBlockedAsync(Guid blockerUserId, CancellationToken ct);

    /// <summary>true quando um dos dois bloqueou o outro.</summary>
    Task<bool> IsBlockedEitherWayAsync(Guid userA, Guid userB, CancellationToken ct);
}
