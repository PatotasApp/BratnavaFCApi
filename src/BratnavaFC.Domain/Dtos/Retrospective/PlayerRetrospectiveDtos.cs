namespace BratnavaFC.Domain.Dtos.Retrospective;

/// <summary>
/// Retrospectiva individual de um jogador num período (ano ou datas), estilo "Wrapped".
/// Tudo já vem calculado: os fronts só escolhem o que mostrar (um bloco nulo = não tem dado).
/// Regras em docs/retrospectiva/RETROSPECTIVA.md.
/// </summary>
public sealed class PlayerRetrospectiveDto
{
    public Guid   GroupId   { get; set; }
    public string GroupName { get; set; } = "";

    public RetrospectivePeriodDto Period { get; set; } = new();
    /// <summary>Anos (Brasília) com jogos na patota, do mais recente ao mais antigo.</summary>
    public List<int> AvailableYears { get; set; } = [];

    /// <summary>Só para admin: jogadores com jogo no período, para escolher de quem ver.</summary>
    public List<RetrospectivePlayerOptionDto>? Players { get; set; }

    public RetrospectivePlayerOptionDto Player { get; set; } = new();
    public bool IsGoalkeeper { get; set; }

    /// <summary>Jogos da patota no período (partidas + campeonatos pelas regras de cada um).</summary>
    public int TotalGames { get; set; }
    /// <summary>Mínimo de jogos para entrar nos rankings de taxa (aproveitamento, gols por jogo...).</summary>
    public int MinGamesForRates { get; set; }
    /// <summary>Quantos jogadores têm o mínimo de jogos (base dos rankings de taxa).</summary>
    public int RatedPlayers { get; set; }

    public RetrospectivePresenceDto Presence { get; set; } = new();
    public RetrospectiveResultsDto  Results  { get; set; } = new();
    public RetrospectiveGoalsDto    Goals    { get; set; } = new();
    public RetrospectiveAssistsDto  Assists  { get; set; } = new();
    /// <summary>Meses com gol registrado na patota (meses sem gol registrado ficam de fora).</summary>
    public List<RetrospectiveMonthDto> Months { get; set; } = [];
    public RetrospectiveDecisiveDto Decisive { get; set; } = new();

    /// <summary>Melhor aproveitamento junto (mesmo time, mín. 4 jogos).</summary>
    public RetrospectivePartnerDto? BestPartner     { get; set; }
    /// <summary>Quem mais jogou do lado dele.</summary>
    public RetrospectivePartnerDto? FrequentPartner { get; set; }
    /// <summary>Melhor retrospecto contra (mín. 4 confrontos, retrospecto positivo).</summary>
    public RetrospectiveRivalDto?   Fregues  { get; set; }
    /// <summary>Pior retrospecto contra (mín. 4 confrontos, só com retrospecto negativo).</summary>
    public RetrospectiveRivalDto?   Carrasco { get; set; }

    public List<RetrospectiveMilestoneDto> Milestones { get; set; } = [];
    public RetrospectiveMvpDto Mvp { get; set; } = new();
    public int OwnGoals { get; set; }

    public RetrospectiveProfileDto Profile { get; set; } = new();
}

public sealed class RetrospectivePeriodDto
{
    /// <summary>Ano (Brasília) quando o período é um ano.</summary>
    public int?     Year  { get; set; }
    /// <summary>Início (UTC, inclusivo).</summary>
    public DateTime Start { get; set; }
    /// <summary>Fim (UTC, exclusivo).</summary>
    public DateTime End   { get; set; }
    public string   Label { get; set; } = "";
    /// <summary>Período ainda em andamento: os números podem mudar.</summary>
    public bool     IsPartial { get; set; }
}

public sealed class RetrospectivePlayerOptionDto
{
    public Guid   PlayerId { get; set; }
    /// <summary>Nome completo cadastrado.</summary>
    public string Name { get; set; } = "";
    /// <summary>Primeiro nome; nome completo se outro jogador da patota tem o mesmo primeiro nome.</summary>
    public string ShortName { get; set; } = "";
    public int    Games { get; set; }
}

public sealed class RetrospectivePresenceDto
{
    public int  Games   { get; set; }
    /// <summary>0–100, arredondado.</summary>
    public int  Percent { get; set; }
    public int? Rank    { get; set; }
    /// <summary>Maior sequência de jogos da patota seguidos sem faltar.</summary>
    public int  LongestStreak { get; set; }
}

public sealed class RetrospectiveResultsDto
{
    public int  Wins   { get; set; }
    public int  Draws  { get; set; }
    public int  Losses { get; set; }
    /// <summary>Aproveitamento 0–100: vitória 3, empate 1.</summary>
    public int  Aproveitamento { get; set; }
    /// <summary>Só para quem tem o mínimo de jogos.</summary>
    public int? Rank { get; set; }
    public int  LongestWinStreak      { get; set; }
    public int  LongestUnbeatenStreak { get; set; }
    /// <summary>Melhor mês (mín. 3 jogos com resultado).</summary>
    public RetrospectiveBestMonthDto? BestMonth { get; set; }
}

public sealed class RetrospectiveBestMonthDto
{
    public int Year   { get; set; }
    public int Month  { get; set; }
    public int Wins   { get; set; }
    public int Draws  { get; set; }
    public int Losses { get; set; }
    public int Aproveitamento { get; set; }
}

public sealed class RetrospectiveGoalsDto
{
    public int  Total { get; set; }
    public int? Rank  { get; set; }
    public RetrospectiveNightDto? BestNight { get; set; }
}

public sealed class RetrospectiveAssistsDto
{
    public int  Total { get; set; }
    public int? Rank  { get; set; }
    public RetrospectiveNightDto? BestNight { get; set; }
    /// <summary>Quem mais fez gol com passe dele.</summary>
    public RetrospectiveCountRefDto? MostAssistedTo { get; set; }
    /// <summary>Quem mais deu passe para ele fazer gol.</summary>
    public RetrospectiveCountRefDto? MostAssistedBy { get; set; }
}

public sealed class RetrospectiveNightDto
{
    public int      Count    { get; set; }
    public DateTime PlayedAt { get; set; }
}

public sealed class RetrospectiveCountRefDto
{
    public Guid   PlayerId { get; set; }
    public string Name  { get; set; } = "";
    public int    Count { get; set; }
}

public sealed class RetrospectiveMonthDto
{
    public int Year    { get; set; }
    public int Month   { get; set; }
    public int Goals   { get; set; }
    public int Assists { get; set; }
}

public sealed class RetrospectiveDecisiveDto
{
    /// <summary>Vezes que fez o primeiro gol da partida.</summary>
    public int Openers     { get; set; }
    /// <summary>Gols que deixaram o vencedor um gol à frente do placar final do perdedor.</summary>
    public int WinningGoals { get; set; }
    /// <summary>Posição em (aberturas + gols da vitória) por jogo; só com o mínimo de jogos.</summary>
    public int? PerGameRank { get; set; }
}

public sealed class RetrospectivePartnerDto
{
    public Guid   PlayerId { get; set; }
    public string Name  { get; set; } = "";
    public int    Games { get; set; }
    public int    Wins  { get; set; }
    public int    Draws { get; set; }
    public int    Losses { get; set; }
    public int    Aproveitamento { get; set; }
}

public sealed class RetrospectiveRivalDto
{
    public Guid   PlayerId { get; set; }
    public string Name   { get; set; } = "";
    public int    Wins   { get; set; }
    public int    Draws  { get; set; }
    public int    Losses { get; set; }
}

public sealed class RetrospectiveMilestoneDto
{
    /// <summary>first_goal, last_goal, goal_century, first_assist, assist_century.</summary>
    public string   Kind     { get; set; } = "";
    /// <summary>Número do gol/assistência na patota no período (1, 100, 200... ou o último).</summary>
    public int      Number   { get; set; }
    public DateTime PlayedAt { get; set; }
}

public sealed class RetrospectiveMvpDto
{
    public int  Mvps  { get; set; }
    public int  Votes { get; set; }
    public int? Rank  { get; set; }
}

public sealed class RetrospectiveProfileDto
{
    /// <summary>fiel, matador, garcom, pequente, craque, decisivo ou paredao.</summary>
    public string Key  { get; set; } = "fiel";
    /// <summary>Posição no dado que definiu o perfil (nula no paredão).</summary>
    public int?   Rank { get; set; }

    public double GoalsPerGame    { get; set; }
    public int?   GoalsPerGameRank { get; set; }
    public double AssistsPerGame  { get; set; }
    public int?   AssistsPerGameRank { get; set; }
    /// <summary>Jogos no gol (para o Paredão).</summary>
    public int    GoalkeeperGames { get; set; }
}
