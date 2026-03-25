using System;
using System.Collections.Generic;

namespace BratnavaFC.Domain.Dtos
{
    public sealed class MatchPostGameDto
    {
        public Guid MatchId { get; init; }
        public short Status { get; init; }

        public int? TeamAGoals { get; init; }
        public int? TeamBGoals { get; init; }

        public List<MatchMvpDto> ComputedMvps { get; init; } = new();

        public List<VoteCountDto> VoteCounts { get; init; } = new();
        public List<VoteDto> Votes { get; init; } = new();
        public List<GoalDto> Goals { get; init; } = new();

        /// <summary>Todos os jogadores não-convidados já votaram.</summary>
        public bool AllVoted { get; init; }

        /// <summary>Jogadores não-convidados que ainda não votaram.</summary>
        public List<PlayerInMatchDto> EligibleVoters { get; init; } = new();

        /// <summary>Jogadores que participam da partida (time A + time B).</summary>
        public List<PlayerInMatchDto> Participants { get; init; } = new();
    }
}
