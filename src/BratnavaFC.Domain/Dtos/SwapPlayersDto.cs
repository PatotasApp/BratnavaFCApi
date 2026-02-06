using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public sealed class SwapPlayersDto
    {
        public Guid PlayerAId { get; set; }
        public Guid PlayerBId { get; set; }
    }

}
