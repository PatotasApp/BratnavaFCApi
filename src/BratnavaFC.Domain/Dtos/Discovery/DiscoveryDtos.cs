using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Domain.Dtos.Discovery;

public sealed record GroupSearchItemDto(Guid GroupId, string Name, string Code, string? LogoUrl,
    string? City, bool IsAcceptingPlayers, int MemberCount, bool IsMember = false, bool IsPublic = true);

public sealed record PlayerSearchItemDto(Guid UserId, string Name, string UserName, string? PhotoUrl,
    string? Position, bool InGroup = false);

public sealed record GroupProfileMemberDto(Guid UserId, string Name, string UserName, string? PhotoUrl);

public sealed record MatchOpeningDto(Guid OpeningId, int Slots, int AvailableSlots, MatchOpeningPosition Position);
public sealed record PublicMatchDto(Guid MatchId, DateTime PlayedAt, string? PlaceName,
    MatchApplicationStatus? ApplicationStatus, List<MatchOpeningDto> Openings, bool Participating = false);

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
    public bool ShowGoalsToGuests { get; set; }
    public List<GroupProfileMemberDto> Members { get; set; } = [];
    public List<PublicMatchDto> UpcomingMatches { get; set; } = [];
}

public sealed record UpdateGroupProfileDto(bool IsPublic, bool IsAcceptingPlayers,
    bool ShowMembers, string? Description, string? City, bool? ShowGoalsToGuests = null);

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
    string? Message, string? DecisionReason, DateTime CreatedAt, DateTimeOffset? RespondedAt = null, short MatchStatus = 0);

public sealed record GuestCandidateDto(Guid PlayerId, string Name, GuestOrigin Origin, bool LinkedToThisUser);

/// <summary>Resposta do jogador convidado: "pending" (ainda nao respondeu), "going" ou "not_going".</summary>
public sealed record GuestMatchSummaryDto(Guid ApplicationId, Guid GroupId, string GroupName, Guid MatchId,
    DateTime PlayedAt, string? PlaceName, short MatchStatus, string MyResponse);

public sealed record GuestMatchPlayerDto(string Name, string? PhotoUrl, short Team, bool IsGoalkeeper, bool IsMe, bool IsMvp = false);
public sealed record GuestMatchGoalDto(string Scorer, string? Assist, short Team, bool OwnGoal);
public sealed record GuestMatchColorDto(string Name, string HexValue);

/// <summary>
/// Visao somente-leitura de uma partida para quem foi aceito como convidado de fora da patota.
/// Nao traz notas, votos, financas nem nada alem do necessario para acompanhar o jogo.
/// </summary>
public sealed class GuestMatchDto
{
    public Guid ApplicationId { get; set; }
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = "";
    public Guid MatchId { get; set; }
    public DateTime PlayedAt { get; set; }
    public string? PlaceName { get; set; }
    public short MatchStatus { get; set; }
    public int? TeamAGoals { get; set; }
    public int? TeamBGoals { get; set; }
    public GuestMatchColorDto? TeamAColor { get; set; }
    public GuestMatchColorDto? TeamBColor { get; set; }
    public string MyResponse { get; set; } = "pending";
    public bool CanChangeAttendance { get; set; }
    public List<GuestMatchPlayerDto> Players { get; set; } = [];

    /// <summary>A patota habilitou a exibicao de gols para convidados (configuracao da patota).</summary>
    public bool GoalsVisible { get; set; }
    public List<GuestMatchGoalDto> Goals { get; set; } = [];
    public int? MyGoals { get; set; }
    public int? MyAssists { get; set; }
}

public sealed record SetGuestAttendanceDto(bool Going);
