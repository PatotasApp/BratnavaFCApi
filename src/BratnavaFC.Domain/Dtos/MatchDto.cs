using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public class MatchDto
    {
        public DateTime PlayedAt { get; set; }
        public int TeamAGoals { get; set; }
        public int TeamBGoals { get; set; }
        public string PlaceName { get; set; } = string.Empty;  
    }
}
