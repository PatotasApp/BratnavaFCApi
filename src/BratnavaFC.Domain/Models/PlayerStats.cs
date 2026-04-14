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
    /// Effective player weight for team balancing.
    ///
    ///   W_base = 0.70 × WinRate_adj + 0.30 × GoalContrib_norm
    ///
    /// For neutral players (fewer than 3 matches), ignored in favour of
    /// <see cref="NeutralOverride"/> ?? 0.50 (see StrategyHelpers.EffectiveWeight).
    ///
    /// Admin dimension ratings (Attack, Defense, Physical) are intentionally excluded from W_base.
    /// They are used by AlgorithmStrategy as dimensional balance constraints, ensuring attack,
    /// defense and physical totals are spread evenly between teams without distorting the
    /// win-rate-based primary balance criterion.
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
    /// When a player has fewer than 3 matches, this holds the override weight.
    /// Priority: OverallRating/10 (if set) → GuestStarRating mapping → null (→ 0.50 default).
    /// Mapping GuestStarRating: 1→0.00  2→0.25  3→0.50  4→0.75  5→1.00
    /// Mapping OverallRating: 0→0.00 … 10→1.00 (linear)
    /// </summary>
    public double? NeutralOverride { get; init; }

    // ── Dimensional ratings (used by AlgorithmStrategy for spread constraints) ──

    /// <summary>
    /// Attack rating normalized to [0, 1] (AttackRating / 10).
    /// Null when the admin has not set an attack rating for this player.
    /// </summary>
    public double? AttackRatingNorm  { get; init; }

    /// <summary>
    /// Defense rating normalized to [0, 1] (DefenseRating / 10).
    /// Null when the admin has not set a defense rating for this player.
    /// </summary>
    public double? DefenseRatingNorm { get; init; }

    /// <summary>
    /// Physical rating normalized to [0, 1] (OverallRating / 10).
    /// Called "físico" in the UI. Null when the admin has not set a physical rating for this player.
    /// </summary>
    public double? PhysicalRatingNorm { get; init; }
}
