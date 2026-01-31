using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public class PlayerRequestDto
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool IsGoalkeeper { get; init; } = false;
    }
}
