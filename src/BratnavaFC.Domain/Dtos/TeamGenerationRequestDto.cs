using BratnavaFC.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public record TeamGenerationRequestDto(List<PlayerRequestDto> Players, StrategyType StrategyType, int PlayersPerTeam, bool IncludeGoalkeepers);
}
