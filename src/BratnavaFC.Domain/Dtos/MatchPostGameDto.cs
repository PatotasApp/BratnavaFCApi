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

        /// <summary>
        /// O jogador autenticado pode votar nesta partida.
        /// Calculado pelo servidor: não é convidado, está num time, não votou ainda e não foi marcado como DidNotPlay.
        /// Null quando o usuário não tem jogador nesta partida.
        /// </summary>
        public bool? CanVote { get; init; }

        /// <summary>True se o jogador autenticado já registrou voto.</summary>
        public bool? HasVoted { get; init; }

        /// <summary>Id do MatchPlayer para quem o usuário autenticado votou (null se ainda não votou).</summary>
        public Guid? MyVotedForMatchPlayerId { get; init; }
    }
}
