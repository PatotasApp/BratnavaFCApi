using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public class SetScoreRequestDto
    {
        public int TeamAGoals { get; init; }
        public int TeamBGoals { get; init; }
    }
}
