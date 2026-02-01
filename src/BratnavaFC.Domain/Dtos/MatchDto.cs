using System;

namespace BratnavaFC.Domain.Dtos
{
    public record MatchDto(DateTime PlayedAt, int TeamAGoals, int TeamBGoals, string PlaceName, Guid? TeamAColorId, Guid? TeamBColorId);
}