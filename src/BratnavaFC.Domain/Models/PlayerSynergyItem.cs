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

        /// <summary>Assists that the subject player gave to this partner.</summary>
        public int AssistsGiven { get; init; }

        /// <summary>Assists that this partner gave to the subject player.</summary>
        public int AssistsReceived { get; init; }
    }
}
