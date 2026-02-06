using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public sealed class AssignTeamsDto
    {
        public List<Guid> TeamAMatchPlayerIds { get; set; } = [];
        public List<Guid> TeamBMatchPlayerIds { get; set; } = [];
    }
}
