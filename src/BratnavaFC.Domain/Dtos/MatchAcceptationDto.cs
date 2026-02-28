using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public sealed class MatchAcceptationDto
    {
        public Guid MatchId { get; init; }
        public short Status { get; init; }
        public List<PlayerInMatchDto> Players { get; init; } = new();
    }
}
