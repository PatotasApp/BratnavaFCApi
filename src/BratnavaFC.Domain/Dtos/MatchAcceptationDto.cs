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

        /// <summary>
        /// O backend decidiu se é possível avançar para o Matchmaking.
        /// São necessários apenas dois confirmados para formar os times. O administrador
        /// continua responsável por decidir se a quantidade é adequada para iniciar.
        /// Frontend deve usar este flag em vez de recalcular contagens.
        /// </summary>
        public bool CanAdvanceToMatchmaking { get; init; }

        public List<PlayerInMatchDto> AcceptedPlayers { get; init; } = new();
        public List<PlayerInMatchDto> RejectedPlayers { get; init; } = new();
        public List<PlayerInMatchDto> PendingPlayers { get; init; } = new();
        public List<PlayerInMatchDto> WaitingPlayers { get; init; } = new();
    }
}
