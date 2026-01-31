using System;
using System.Collections.Generic;

namespace BratnavaFC.Domain.Models;

public class Player
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = null!;
    public bool IsGoalkeeper { get; init; }
}