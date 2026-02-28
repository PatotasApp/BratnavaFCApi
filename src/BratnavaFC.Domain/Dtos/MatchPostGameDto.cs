using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public sealed class MatchPostGameDto
    {
        public Guid MatchId { get; init; }
        public short Status { get; init; }

        public int? TeamAGoals { get; init; }
        public int? TeamBGoals { get; init; }

        public MatchMvpDto? ComputedMvp { get; init; }

        public List<VoteCountDto> VoteCounts { get; init; } = new();
        public List<GoalDto> Goals { get; init; } = new();
    }
}
