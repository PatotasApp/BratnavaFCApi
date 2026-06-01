using System;
using System.Collections.Generic;

namespace BratnavaFC.Domain.Dtos
{
    public sealed class MatchMatchMakingDto
    {
        public Guid MatchId { get; init; }
        public short Status { get; init; }

        public TeamColorDto? TeamAColor { get; init; }
        public TeamColorDto? TeamBColor { get; init; }

        public bool ColorsLocked { get; init; }

        public List<PlayerInMatchDto> TeamAPlayers { get; init; } = new();
        public List<PlayerInMatchDto> TeamBPlayers { get; init; } = new();
        public List<PlayerInMatchDto> UnassignedPlayers { get; init; } = new();

        /// <summary>Union of TeamA + TeamB — jogadores que participam da partida.</summary>
        public List<PlayerInMatchDto> Participants { get; init; } = new();

        /// <summary>True quando ambos os times têm ao menos 1 jogador.</summary>
        public bool TeamsAssigned { get; init; }

        /// <summary>
        /// O backend decidiu se é possível iniciar a partida.
        /// Calculado como: TeamsAssigned (mesma regra do MatchEntity.Start()).
        /// Frontend deve usar este flag em vez de verificar contagens localmente.
        /// </summary>
        public bool CanStartMatch { get; init; }
    }
}
