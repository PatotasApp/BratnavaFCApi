using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public record SetScoreRequestDto(int TeamAGoals, int TeamBGoals);
}
