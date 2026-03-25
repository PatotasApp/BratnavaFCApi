using System;
using System.Collections.Generic;

namespace BratnavaFC.Domain.Dtos
{
    public sealed class MatchAcceptationDto
    {
        public Guid MatchId { get; init; }
        public short Status { get; init; }

        public int MaxPlayers { get; init; }
        public bool AcceptedOverLimit { get; init; }

        public List<PlayerInMatchDto> AcceptedPlayers { get; init; } = new();
        public List<PlayerInMatchDto> RejectedPlayers { get; init; } = new();
        public List<PlayerInMatchDto> PendingPlayers { get; init; } = new();
    }
}
