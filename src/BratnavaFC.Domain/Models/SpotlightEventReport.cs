namespace BratnavaFC.Domain.Models;

/// <summary>
/// Tudo o que o Spotlight do telão mostra, já calculado. Listas de ranking trazem todos até
/// o 5º lugar (empatados dividem a colocação: 1, 1, 3); quem exibe decide como resumir.
/// Só os admins da patota veem o Spotlight, então nada é escondido.
/// </summary>
public sealed class SpotlightEventReport
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public SpotlightSettings Settings { get; set; } = new();

    public SpotlightPeriodInfo Period { get; set; } = new();
    /// <summary>Mínimo de jogos usado em médias e aproveitamento.</summary>
    public int MinGames { get; set; }
    /// <summary>Mínimo de jogos juntos para a melhor dupla.</summary>
    public int PairMinGames { get; set; }

    public SpotlightNumbers Numbers { get; set; } = new();
    public SpotlightRankings Rankings { get; set; } = new();
    public SpotlightAverages? Averages { get; set; }
    public SpotlightMilestones? Milestones { get; set; }
    public SpotlightRecords Records { get; set; } = new();
    public SpotlightStreaks Streaks { get; set; } = new();
    public SpotlightDecisive? Decisive { get; set; }
    public SpotlightPairs Pairs { get; set; } = new();
    public List<SpotlightPlayer> Players { get; set; } = new();
}

public sealed class SpotlightPeriodInfo
{
    public SpotlightPeriodMode Mode { get; set; }
    public int? SeasonYear { get; set; }
    public DateOnly? SinceDate { get; set; }
    public int? LastGames { get; set; }
    public bool OnlyMonthly { get; set; }
    public bool IncludeChampionships { get; set; }
    /// <summary>Primeiro e último jogo do período (nulos sem jogos).</summary>
    public DateTime? FirstGameAt { get; set; }
    public DateTime? LastGameAt { get; set; }
}

public sealed class SpotlightNumbers
{
    public int Games { get; set; }
    /// <summary>Gols registrados (com autor), inclusive gols contra.</summary>
    public int? Goals { get; set; }
    public int? GamesWithGoals { get; set; }
    /// <summary>Gols registrados por jogo, só sobre as partidas que têm gols registrados.</summary>
    public double? GoalsPerGame { get; set; }
    public int? Assists { get; set; }
    /// <summary>Gols (sem gol contra) + assistências.</summary>
    public int? Participations { get; set; }
    public int Players { get; set; }
    public int Draws { get; set; }
    public int? OwnGoals { get; set; }
    public int MvpVotes { get; set; }
    public int Replays { get; set; }
    /// <summary>Partida com mais gols registrados.</summary>
    public SpotlightScoreline? MostGoalsGame { get; set; }
    /// <summary>Maior goleada pelo placar.</summary>
    public SpotlightScoreline? BiggestWin { get; set; }
}

/// <summary>Placar de uma partida, com o lado de maior placar primeiro.</summary>
public sealed class SpotlightScoreline
{
    public DateTime PlayedAt { get; set; }
    public int GoalsA { get; set; }
    public int GoalsB { get; set; }
}

public sealed class SpotlightRankRow
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
    public int Rank { get; set; }
}

public sealed class SpotlightRankings
{
    public List<SpotlightRankRow>? Goals { get; set; }
    public List<SpotlightRankRow>? Assists { get; set; }
    public List<SpotlightRankRow>? Participations { get; set; }
    public List<SpotlightRankRow> Presences { get; set; } = new();
    public List<SpotlightRankRow> Wins { get; set; } = new();
    public List<SpotlightRankRow> Mvps { get; set; } = new();
    /// <summary>Aproveitamento (V=3, E=1) em %, com o mínimo de jogos.</summary>
    public List<SpotlightRankRow> PointsRate { get; set; } = new();
}

public sealed class SpotlightAverages
{
    public List<SpotlightRankRow> Goals { get; set; } = new();
    public List<SpotlightRankRow> Assists { get; set; } = new();
    public List<SpotlightRankRow> Participations { get; set; } = new();
}

public sealed class SpotlightGoalMoment
{
    /// <summary>Número do gol (ou da assistência) no período.</summary>
    public int Number { get; set; }
    public DateTime PlayedAt { get; set; }
    public string ScorerName { get; set; } = string.Empty;
    public string? AssistName { get; set; }
    public bool IsOwnGoal { get; set; }
    /// <summary>Horário anotado, em segundos ("21:01" = 1261).</summary>
    public int? TimeSeconds { get; set; }
    /// <summary>Placar final do jogo do ponto de vista de quem ganhou o gol (nulo sem placar).</summary>
    public int? ScoreFor { get; set; }
    public int? ScoreAgainst { get; set; }
}

public sealed class SpotlightMilestones
{
    public SpotlightGoalMoment? FirstGoal { get; set; }
    public SpotlightGoalMoment? LastGoal { get; set; }
    public List<SpotlightGoalMoment> GoalCenturies { get; set; } = new();
    public SpotlightGoalMoment? FirstAssist { get; set; }
    public List<SpotlightGoalMoment> AssistCenturies { get; set; } = new();
    public int TotalAssists { get; set; }
    public int OwnGoalsTotal { get; set; }
    public List<SpotlightCountRow> OwnGoalClub { get; set; } = new();
}

public sealed class SpotlightCountRow
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class SpotlightHaul
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Goals { get; set; }
    public DateTime PlayedAt { get; set; }
    public int Rank { get; set; }
}

public sealed class SpotlightComeback
{
    public DateTime PlayedAt { get; set; }
    /// <summary>Maior desvantagem do vencedor durante o jogo.</summary>
    public int Deficit { get; set; }
    public int WinnerGoals { get; set; }
    public int LoserGoals { get; set; }
    public int Rank { get; set; }
}

public sealed class SpotlightRecords
{
    public List<SpotlightHaul>? Hauls { get; set; }
    public List<SpotlightComeback> Comebacks { get; set; } = new();
    public int ComebacksTotal { get; set; }
}

public sealed class SpotlightStreaks
{
    public List<SpotlightRankRow> Presence { get; set; } = new();
    public List<SpotlightRankRow> Wins { get; set; } = new();
    public List<SpotlightRankRow> Unbeaten { get; set; } = new();
}

public sealed class SpotlightDecisive
{
    public List<SpotlightRankRow> OpenedScoring { get; set; } = new();
    public List<SpotlightRankRow> WinningGoals { get; set; } = new();
}

public sealed class SpotlightPairRow
{
    public Guid PlayerAId { get; set; }
    public string PlayerAName { get; set; } = string.Empty;
    public Guid PlayerBId { get; set; }
    public string PlayerBName { get; set; } = string.Empty;
    public int Games { get; set; }
    /// <summary>Aproveitamento juntos, em %.</summary>
    public int PointsRate { get; set; }
    public int Rank { get; set; }
}

/// <summary>Garçom (A) → artilheiro (B).</summary>
public sealed class SpotlightAssistPairRow
{
    public Guid AssistId { get; set; }
    public string AssistName { get; set; } = string.Empty;
    public Guid ScorerId { get; set; }
    public string ScorerName { get; set; } = string.Empty;
    public int Goals { get; set; }
    public int Rank { get; set; }
}

public sealed class SpotlightPairs
{
    public List<SpotlightPairRow> MostTogether { get; set; } = new();
    public List<SpotlightPairRow> BestTogether { get; set; } = new();
    public List<SpotlightAssistPairRow>? AssistCombos { get; set; }
}

public sealed class SpotlightPartner
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Games { get; set; }
    public int PointsRate { get; set; }
}

public sealed class SpotlightRival
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
}

/// <summary>
/// O dado em que o jogador fica mais bem colocado na patota. Kind: goals, assists,
/// participations, presences, wins, mvps, pointsRate, presenceStreak, winStreak,
/// unbeatenStreak, openedScoring, winningGoals, haul.
/// </summary>
public sealed class SpotlightFact
{
    public string Kind { get; set; } = string.Empty;
    public double Value { get; set; }
    public int Rank { get; set; }
    /// <summary>Outro jogador tem o mesmo valor (mesma colocação).</summary>
    public bool Tied { get; set; }
}

public sealed class SpotlightPlayer
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsGoalkeeper { get; set; }
    public bool IsGuest { get; set; }
    public int Games { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public int? Goals { get; set; }
    public int? Assists { get; set; }
    public int? OwnGoals { get; set; }
    public int Mvps { get; set; }
    /// <summary>Aproveitamento em % (nulo sem jogos com resultado).</summary>
    public int? PointsRate { get; set; }
    public List<SpotlightPartner> BestPartners { get; set; } = new();
    public List<SpotlightPartner> WorstPartners { get; set; } = new();
    /// <summary>Retrospecto negativo do jogador contra eles (times opostos).</summary>
    public List<SpotlightRival> Nemeses { get; set; } = new();
    /// <summary>Retrospecto positivo do jogador contra eles.</summary>
    public List<SpotlightRival> Victims { get; set; } = new();
    /// <summary>Para quem o jogador mais deu assistência.</summary>
    public List<SpotlightCountRow>? AssistedTo { get; set; }
    /// <summary>Quem mais deu assistência para o jogador.</summary>
    public List<SpotlightCountRow>? AssistedBy { get; set; }
    public SpotlightFact? Fact { get; set; }
}
