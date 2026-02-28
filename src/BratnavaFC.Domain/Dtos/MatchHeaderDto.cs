using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public sealed class MatchHeaderDto
    {
        public Guid MatchId { get; init; }
        public Guid GroupId { get; init; }
        public DateTime PlayedAt { get; init; }
        public string PlaceName { get; init; } = string.Empty;

        public short Status { get; init; }
        public string StatusName { get; init; } = string.Empty;

        public int? TeamAGoals { get; init; }
        public int? TeamBGoals { get; init; }
    }
}
