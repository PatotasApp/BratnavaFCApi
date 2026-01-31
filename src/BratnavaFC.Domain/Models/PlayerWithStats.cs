using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Models
{
    public readonly record struct PlayerWithStats(Player Player, PlayerStats Stats);
}
