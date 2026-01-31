using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public class SetMatchColorsRequestDto
    {
        public Guid? TeamAColorId { get; init; }
        public Guid? TeamBColorId { get; init; }
        public bool Randomize { get; init; } = false;
    }
}
