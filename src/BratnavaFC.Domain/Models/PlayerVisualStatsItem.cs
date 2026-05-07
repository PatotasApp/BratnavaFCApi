using BratnavaFC.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Models
{
    public sealed class PlayerVisualStatsItem
    {
        public Guid PlayerId { get; init; }
        public string Name { get; init; } = string.Empty;

        public Status Status { get; init; }
        public bool IsGoalkeeper { get; init; }

        public int GamesPlayed { get; init; }
        public int Wins { get; init; }
        public int Ties { get; init; }
        public int Losses { get; init; }
        public double WinRate { get; init; }

        public int Mvps { get; init; }
        public int MvpVotes { get; init; }

        public int Goals { get; init; }
        public int Assists { get; init; }
        public int OwnGoals { get; init; }

        // Synergy “visual”: com nomes + quantidade de jogos juntos + winrate juntos
        public List<PlayerSynergyItem> Synergies { get; init; } = new();
    }
}
