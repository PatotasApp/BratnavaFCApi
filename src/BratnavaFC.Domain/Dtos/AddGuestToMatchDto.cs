namespace BratnavaFC.Domain.Dtos;

public sealed record AddGuestToMatchDto(string Name, bool IsGoalkeeper, int? GuestStarRating = null);
