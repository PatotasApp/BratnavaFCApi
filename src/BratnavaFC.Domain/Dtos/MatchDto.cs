using System;

namespace BratnavaFC.Domain.Dtos
{
    public class MatchDto
    {
        public DateTime PlayedAt { get; set; }
        public int TeamAGoals { get; set; }
        public int TeamBGoals { get; set; }
        public string PlaceName { get; set; } = string.Empty;

        // optional color ids for teams
        public Guid? TeamAColorId { get; set; }
        public Guid? TeamBColorId { get; set; }
    }
}