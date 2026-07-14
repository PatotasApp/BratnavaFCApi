namespace BratnavaFC.Api.Realtime;

public sealed record RealtimeEventDto(
    string Type,
    Guid GroupId,
    Guid? MatchId,
    Guid? PollId,
    string Reason,
    DateTime OccurredAtUtc);
