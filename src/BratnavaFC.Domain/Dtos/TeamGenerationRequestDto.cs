using BratnavaFC.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public class TeamGenerationRequestDto
    {
        public List<PlayerRequestDto> Players { get; init; } = new();
        public StrategyType StrategyType { get; init; } = StrategyType.Random;
        public int PlayersPerTeam { get; init; } = 5;
        public bool IncludeGoalkeepers { get; init; } = true;
    }
}
