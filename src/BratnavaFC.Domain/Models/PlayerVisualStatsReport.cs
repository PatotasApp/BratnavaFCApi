using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Models
{
    public sealed class PlayerVisualStatsReport
    {
        public Guid GroupId { get; init; }
        public int TotalMatchesConsidered { get; init; }           // total de partidas em que pelo menos 1 player do grupo participou
        public int TotalFinalizedMatches { get; init; }            // partidas finalizadas
        public int TotalMatchesWithScore { get; init; }            // partidas com placar definido
        public List<PlayerVisualStatsItem> Players { get; init; } = new();
    }
}
