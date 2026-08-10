namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Contribuição estatística imutável de um jogador em uma partida finalizada.
/// É a camada intermediária entre a partida (fonte da verdade) e as projeções.
/// </summary>
public sealed class PlayerMatchStatContributionEntity : BaseEntity
{
    public Guid MatchId { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid PlayerId { get; private set; }
    public int Season { get; private set; }
    public DateTimeOffset PlayedAt { get; private set; }
    public string Result { get; private set; } = "D";
    public bool IsGoalkeeper { get; private set; }
    public int Games { get; private set; }
    public int Wins { get; private set; }
    public int Goals { get; private set; }
    public int Assists { get; private set; }
    public int Mvps { get; private set; }
    public int OwnGoals { get; private set; }
    public int CleanSheets { get; private set; }
    public int HatTricks { get; private set; }
    public int Pokers { get; private set; }
    public int FiveGoalGames { get; private set; }
    public int GoalAndAssistGames { get; private set; }
    public int ThreeAssistGames { get; private set; }

    private PlayerMatchStatContributionEntity() { }

    public PlayerMatchStatContributionEntity(
        Guid matchId, Guid groupId, Guid playerId, int season,
        DateTimeOffset playedAt, string result, bool isGoalkeeper,
        int goals, int assists, int mvps, int ownGoals, int goalsConceded)
    {
        MatchId = matchId;
        GroupId = groupId;
        PlayerId = playerId;
        Season = season;
        PlayedAt = playedAt;
        Result = result;
        IsGoalkeeper = isGoalkeeper;
        Games = 1;
        Wins = result == "W" ? 1 : 0;
        Goals = goals;
        Assists = assists;
        Mvps = mvps;
        OwnGoals = ownGoals;
        CleanSheets = isGoalkeeper && goalsConceded == 0 ? 1 : 0;
        HatTricks = goals == 3 ? 1 : 0;
        Pokers = goals == 4 ? 1 : 0;
        FiveGoalGames = goals >= 5 ? 1 : 0;
        GoalAndAssistGames = goals > 0 && assists > 0 ? 1 : 0;
        ThreeAssistGames = assists >= 3 ? 1 : 0;
    }
}
