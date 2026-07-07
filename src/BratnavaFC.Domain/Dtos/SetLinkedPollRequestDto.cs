namespace BratnavaFC.Domain.Dtos;

/// <summary>Vincula ({ pollId }) ou desvincula ({ pollId: null }) uma votação de uma partida.</summary>
public sealed record SetLinkedPollRequestDto(Guid? PollId);
