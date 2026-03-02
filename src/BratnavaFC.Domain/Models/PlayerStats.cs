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

    /// <summary>
    /// When a guest has a star rating (1–5) and fewer than 3 matches,
    /// this holds the proportional win-rate to use instead of the default neutral 0.50.
    /// Mapping: 1→0.00  2→0.25  3→0.50  4→0.75  5→1.00
    /// </summary>
    public double? NeutralOverride { get; init; }
}
