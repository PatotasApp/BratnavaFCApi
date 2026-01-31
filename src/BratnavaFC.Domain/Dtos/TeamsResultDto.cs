using System;
using System.Collections.Generic;

namespace BratnavaFC.Domain.Dtos;

public class TeamsResultDto
{
    public List<Guid> TeamA { get; init; } = new();
    public List<Guid> TeamB { get; init; } = new();
    public List<Guid> Unassigned { get; init; } = new();
}