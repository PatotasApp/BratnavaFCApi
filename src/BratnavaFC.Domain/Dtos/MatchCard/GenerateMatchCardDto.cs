namespace BratnavaFC.Domain.Dtos.MatchCard;

/// <summary>
/// Request para gerar card de partida via ChatGPT.
/// </summary>
public class GenerateMatchCardDto
{
    /// <summary>"match_preview" ou "match_result"</summary>
    public string Template { get; set; } = "match_preview";

    // ── Times ────────────────────────────────────────────────────────────────
    public string TeamAName { get; set; } = "";
    public string TeamAColorHex { get; set; } = "";
    public List<MatchCardPlayerDto> TeamAPlayers { get; set; } = [];

    public string TeamBName { get; set; } = "";
    public string TeamBColorHex { get; set; } = "";
    public List<MatchCardPlayerDto> TeamBPlayers { get; set; } = [];

    // ── Data/hora ────────────────────────────────────────────────────────────
    public string? PlayedAt { get; set; }

    // ── Só para match_result ─────────────────────────────────────────────────
    public int? TeamAGoals { get; set; }
    public int? TeamBGoals { get; set; }
    public string? MvpName { get; set; }
    public string? WinnerTeamName { get; set; }
}

public class MatchCardPlayerDto
{
    public string Name { get; set; } = "";
    public bool IsGoalkeeper { get; set; }
}
