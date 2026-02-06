using System;
using System.Collections.Generic;

namespace BratnavaFC.Domain.Models;

public class PlayerStats
{
    public Guid PlayerId { get; init; }
    public string Name { get; set; }
    public int Wins { get; init; }
    public int Ties { get; init; }
    public int Losses { get; init; }
    public double WinRate { get; init; } // 0..1

    public Dictionary<Guid, double> SynergyWith { get; init; } = new();
}