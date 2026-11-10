using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Domain.Dtos.Discovery;

public sealed record GroupSearchItemDto(Guid GroupId, string Name, string Code, string? LogoUrl,
    string? City, bool IsAcceptingPlayers, int MemberCount);

public sealed record PlayerSearchItemDto(Guid UserId, string Name, string UserName, string? PhotoUrl,
    string? Position);

public sealed record GroupProfileMemberDto(Guid UserId, string Name, string UserName, string? PhotoUrl);

public sealed record MatchOpeningDto(Guid OpeningId, int Slots, int AvailableSlots, MatchOpeningPosition Position);
public sealed record PublicMatchDto(Guid MatchId, DateTime PlayedAt, string? PlaceName,
    MatchApplicationStatus? ApplicationStatus, List<MatchOpeningDto> Openings);

public sealed class GroupProfileDto
{
    public Guid GroupId { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string? Description { get; set; }
    public string? City { get; set; }
    public DayOfWeek? DefaultDayOfWeek { get; set; }
    public TimeSpan? DefaultKickoffTime { get; set; }
    public int MemberCount { get; set; }
    public bool IsPublic { get; set; }
    public bool IsAcceptingPlayers { get; set; }
    public bool ShowMembers { get; set; }
    public List<GroupProfileMemberDto> Members { get; set; } = [];
    public List<PublicMatchDto> UpcomingMatches { get; set; } = [];
}

public sealed record UpdateGroupProfileDto(bool IsPublic, bool IsAcceptingPlayers,
    bool ShowMembers, string? Description, string? City);

public sealed record CreateMatchApplicationDto(Guid MatchId, string? Message = null);
public sealed record CreateAdminMatchInviteDto(Guid MatchId, Guid UserId, Guid? GuestPlayerId,
    Guid? OpeningId = null, string? Message = null);
public sealed record ApproveMatchApplicationDto(Guid? GuestPlayerId, string? Reason = null);
public sealed record RejectMatchApplicationDto(string? Reason = null);
public sealed record UpsertMatchOpeningDto(int Slots, MatchOpeningPosition Position);

public sealed record MatchApplicationDto(Guid Id, Guid GroupId, string GroupName, Guid MatchId,
    DateTime PlayedAt, string? PlaceName, Guid UserId, string PlayerName, string UserName,
    string? PhotoUrl, MatchApplicationSource Source, MatchApplicationStatus Status,
    Guid? GuestPlayerId, Guid? OpeningId, MatchOpeningPosition? OpeningPosition,
    string? Message, string? DecisionReason, DateTime CreatedAt);

public sealed record GuestCandidateDto(Guid PlayerId, string Name, GuestOrigin Origin, bool LinkedToThisUser);
