using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public sealed class UpdateTeamColorDto
    {
        public string Name { get; set; } = string.Empty;
        public string HexValue { get; set; } = string.Empty;
    }
}
