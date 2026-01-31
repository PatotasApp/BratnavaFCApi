using System;
using System.Collections.Generic;

namespace BratnavaFC.Domain.Dtos;

public record TeamsResultDto(List<Guid> TeamA, List<Guid> TeamB, List<Guid> Unassigned);
