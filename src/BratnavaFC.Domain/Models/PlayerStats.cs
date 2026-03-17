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

    /// <summary>
    /// Effective player weight for team balancing: W_base = 0.70 × WinRate_adj + 0.30 × GoalContrib_norm.
    /// For neutral players (fewer than 3 matches) the value is set but ignored in favour of
    /// <see cref="NeutralOverride"/> ?? 0.50 (see <c>EffectiveWinRate</c> in StrategyHelpers).
    /// </summary>
    public double WinRate { get; init; } // W_base ∈ [0, 1]

    public int Goals { get; init; }
    public int Assists { get; init; }

    /// <summary>
    /// Pairwise synergy map: partnerId → Synergy_eff.
    /// Synergy_eff = Confidence × (WinRate_together_adj − expected_baseline), range ≈ [−0.5, +0.5].
    /// Positive values indicate teammates who win together more than their individual rates predict.
    /// Zero means no shared history (neutral).
    /// </summary>
    public Dictionary<Guid, double> SynergyWith { get; init; } = new();

    /// <summary>
    /// When a guest has a star rating (1–5) and fewer than 3 matches,
    /// this holds the proportional win-rate to use instead of the default neutral 0.50.
    /// Mapping: 1→0.00  2→0.25  3→0.50  4→0.75  5→1.00
    /// </summary>
    public double? NeutralOverride { get; init; }
}
