namespace BratnavaFC.Domain.Dtos;

public record VoteRequestDto(Guid VoterPlayerId, Guid VotedPlayerId);
