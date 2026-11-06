using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Moderation;

public sealed record CreateContentReportDto(
    ContentReportTargetType TargetType, Guid TargetId, Guid? GroupId, string Reason, string? Details);

public sealed record ContentReportDto(
    Guid Id, DateTime CreatedAt, Guid ReporterUserId, string ReporterName, Guid? GroupId, string? GroupName,
    ContentReportTargetType TargetType, Guid TargetId, string Reason, string? Details,
    bool IsResolved, DateTime? ResolvedAt, string? ResolutionNote);

public sealed record ResolveContentReportDto(string? Note);

public sealed record BlockedUserDto(Guid UserId, string Name, DateTime BlockedAt);
