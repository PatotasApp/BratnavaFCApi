using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Models
{
    public sealed class PlayerSynergyItem
    {
        public Guid WithPlayerId { get; init; }
        public string WithPlayerName { get; init; } = string.Empty;

        public int MatchesTogether { get; init; }
        public int WinsTogether { get; init; }
        public double WinRateTogether { get; init; }               // WinsTogether / MatchesTogether
    }
}
