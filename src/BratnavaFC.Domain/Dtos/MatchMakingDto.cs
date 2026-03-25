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
    }
}
