using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public sealed class MatchMatchMakingDto
    {
        public Guid MatchId { get; init; }
        public short Status { get; init; }

        public TeamColorDto? TeamAColor { get; init; }
        public TeamColorDto? TeamBColor { get; init; }

        public List<PlayerInMatchDto> TeamAPlayers { get; init; } = new();
        public List<PlayerInMatchDto> TeamBPlayers { get; init; } = new();
        public List<PlayerInMatchDto> UnassignedPlayers { get; init; } = new();
    }
}
