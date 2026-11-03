namespace BratnavaFC.Domain.Dtos.TeamBuilder;

/// <summary>
/// Recorte das partidas consideradas. Os filtros se somam: temporada e datas primeiro,
/// depois "últimos N jogos" sobre o que sobrou. Datas no horário de Brasília.
/// </summary>
public class TeamBuilderFilterDto
{
    /// <summary>Ano da temporada (pela data do jogo, horário de Brasília). Nulo = todas.</summary>
    public int?      Season              { get; set; }
    public DateOnly? From                { get; set; }
    public DateOnly? To                  { get; set; }
    /// <summary>Só os últimos N jogos da patota dentro do recorte.</summary>
    public int?      LastMatches         { get; set; }
    /// <summary>Jogos de campeonato que contam como partida normal (ChampionshipStatsSource).</summary>
    public bool      IncludeChampionship { get; set; } = true;
}

public sealed class TeamBuilderRequestDto : TeamBuilderFilterDto
{
    public List<Guid> PlayerIds { get; set; } = [];
}

/// <summary>Lista de escolha: jogadores da patota com quantos jogos fizeram no recorte.</summary>
public sealed class TeamBuilderPlayersDto
{
    public int                         PeriodMatches { get; set; }
    /// <summary>Temporadas (anos) com partidas, da mais recente para a mais antiga.</summary>
    public List<int>                   Seasons       { get; set; } = [];
    public List<TeamBuilderPlayerItemDto> Players    { get; set; } = [];
}

public sealed class TeamBuilderPlayerItemDto
{
    public Guid   Id           { get; set; }
    public string Name         { get; set; } = "";
    /// <summary>Primeiro nome; nome completo quando outro jogador da patota tem o mesmo primeiro nome.</summary>
    public string ShortName    { get; set; } = "";
    public bool   IsGoalkeeper { get; set; }
    public int    Games        { get; set; }
}

/// <summary>
/// "Juntos" = partidas em que TODOS os selecionados estavam escalados no MESMO time
/// (Team 1/2 e !DidNotPlay, como nas Estatísticas). Aproveitamento: V=3, E=1, em %.
/// </summary>
public sealed class TeamBuilderStatsDto
{
    public bool                    NeverPlayedTogether  { get; set; }
    public int                     TotalMatches         { get; set; }
    public int                     Wins                 { get; set; }
    public int                     Draws                { get; set; }
    public int                     Losses               { get; set; }
    public int                     GoalsScored          { get; set; }
    public int                     GoalsConceded        { get; set; }
    public int                     GoalsScoredByPlayers { get; set; }
    public List<AssistPairDto>      AssistPairs          { get; set; } = [];
    public List<SelectedPlayerDto>  Players              { get; set; } = [];
    public List<PlayerBreakdownDto> PlayerBreakdown      { get; set; } = [];
    public List<FormationMatchDto>  Matches              { get; set; } = [];

    /// <summary>Partidas da patota no recorte.</summary>
    public int     PeriodMatches              { get; set; }
    /// <summary>Partidas em que todos estavam em campo, mas não no mesmo time (não contam).</summary>
    public int     OppositeTeamsMatches       { get; set; }
    /// <summary>Aproveitamento juntos (0–100).</summary>
    public int     PointsPct                  { get; set; }
    /// <summary>Média do aproveitamento individual de cada um em todos os jogos do recorte.</summary>
    public int     AverageIndividualPointsPct { get; set; }
    public int     GoalDifference             { get; set; }
    public double  GoalsPerMatch              { get; set; }
    /// <summary>Gols registrados do time nessas partidas (inclui gol contra a favor).</summary>
    public int     TeamRegisteredGoals        { get; set; }
    public FormationMatchDto? BiggestWin      { get; set; }
    public FormationMatchDto? WorstLoss       { get; set; }
    public FormationMatchDto? LastMatch       { get; set; }
    /// <summary>Sequência atual: resultado do último jogo (1/0/-1) repetido Count vezes.</summary>
    public StreakDto?         CurrentStreak   { get; set; }
    public int                BestWinStreak   { get; set; }
    /// <summary>"Pedra no sapato": adversário com mais derrotas que vitórias (mín. 3 jogos).</summary>
    public OpponentRecordDto? Nemesis         { get; set; }
    /// <summary>"Freguês": adversário com mais vitórias que derrotas (mín. 3 jogos).</summary>
    public OpponentRecordDto? FavoriteVictim  { get; set; }
    /// <summary>Frases prontas; trechos entre ** são destaque.</summary>
    public List<string>       Curiosities     { get; set; } = [];
}

/// <summary>Contribuição individual de cada selecionado nas partidas em que jogaram juntos.</summary>
public sealed class PlayerBreakdownDto
{
    public Guid   Id           { get; set; }
    public string Name         { get; set; } = "";
    public string ShortName    { get; set; } = "";
    public bool   IsGoalkeeper { get; set; }
    public int    Games        { get; set; }
    public int    Wins         { get; set; }
    public int    Draws        { get; set; }
    public int    Losses       { get; set; }
    public double WinRate      { get; set; }
    public int    Goals        { get; set; }
    public int    Assists      { get; set; }
    public int    Mvps         { get; set; }
    /// <summary>Jogos e aproveitamento do jogador em todas as partidas do recorte ("geral").</summary>
    public int    OverallGames     { get; set; }
    public int    OverallPointsPct { get; set; }
}

/// <summary>Uma partida em que a formação selecionada jogou junta (perspectiva do time deles).</summary>
public sealed class FormationMatchDto
{
    public Guid           MatchId         { get; set; }
    public DateTimeOffset PlayedAt        { get; set; }
    public int            GoalsFor        { get; set; }
    public int            GoalsAgainst    { get; set; }
    public string?        PlaceName       { get; set; }
    public string?        ColorForHex     { get; set; }
    public string?        ColorAgainstHex { get; set; }
    public int            Result          { get; set; } // 1 vitória, 0 empate, -1 derrota
    public bool           IsChampionship  { get; set; }
}

public sealed class StreakDto
{
    public int Result { get; set; } // 1 vitória, 0 empate, -1 derrota
    public int Count  { get; set; }
}

public sealed class OpponentRecordDto
{
    public Guid   PlayerId  { get; set; }
    public string Name      { get; set; } = "";
    public string ShortName { get; set; } = "";
    public int    Games     { get; set; }
    public int    Wins      { get; set; }
    public int    Draws     { get; set; }
    public int    Losses    { get; set; }
    public int    PointsPct { get; set; }
}

public sealed class AssistPairDto
{
    public Guid   AssisterId        { get; set; }
    public string AssisterName      { get; set; } = "";
    public string AssisterShortName { get; set; } = "";
    public Guid   ScorerId          { get; set; }
    public string ScorerName        { get; set; } = "";
    public string ScorerShortName   { get; set; } = "";
    public int    Count             { get; set; }
}

public sealed class SelectedPlayerDto
{
    public Guid   Id           { get; set; }
    public string Name         { get; set; } = "";
    public string ShortName    { get; set; } = "";
    public bool   IsGoalkeeper { get; set; }
}
