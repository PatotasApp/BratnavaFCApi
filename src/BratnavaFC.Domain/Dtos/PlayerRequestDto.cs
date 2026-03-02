using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public record PlayerRequestDto(Guid Id, string Name, bool IsGoalkeeper, int? GuestStarRating = null);
}
