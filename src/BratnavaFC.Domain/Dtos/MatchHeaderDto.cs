using System;

namespace BratnavaFC.Domain.Dtos
{
    public sealed class MatchHeaderDto
    {
        public Guid MatchId { get; init; }
        public Guid GroupId { get; init; }
        public DateTime PlayedAt { get; init; }
        public string PlaceName { get; init; } = string.Empty;

        public short Status { get; init; }
        public string StatusName { get; init; } = string.Empty;

        /// <summary>Chave de etapa para o frontend: create | accept | teams | playing | ended | post | done</summary>
        public string StepKey { get; init; } = "create";

        /// <summary>Verdadeiro quando é possível voltar uma etapa (status > Created).</summary>
        public bool CanRewind { get; init; }

        public int? TeamAGoals { get; init; }
        public int? TeamBGoals { get; init; }
    }
}
