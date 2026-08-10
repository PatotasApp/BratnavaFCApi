namespace BratnavaFC.Domain.Entities;

/// <summary>Totais materializados por jogador. Season=0 representa a carreira na patota.</summary>
public sealed class PlayerStatProjectionEntity : BaseEntity
{
    public Guid GroupId { get; private set; }
    public Guid PlayerId { get; private set; }
    public int Season { get; private set; }
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
    public int UnbeatenFiveRuns { get; private set; }
    public int UnbeatenTenRuns { get; private set; }
    public int FiveWinRuns { get; private set; }
    public int ThreeCleanSheetRuns { get; private set; }
    public DateTimeOffset ProjectedAt { get; private set; }
    public int WinRatePct => Games == 0 ? 0 : (int)Math.Round(Wins * 100.0 / Games);

    private PlayerStatProjectionEntity() { }

    public PlayerStatProjectionEntity(
        Guid groupId, Guid playerId, int season,
        IEnumerable<PlayerMatchStatContributionEntity> rows)
    {
        var list = rows.OrderBy(x => x.PlayedAt).ToList();
        GroupId = groupId;
        PlayerId = playerId;
        Season = season;
        Games = list.Sum(x => x.Games);
        Wins = list.Sum(x => x.Wins);
        Goals = list.Sum(x => x.Goals);
        Assists = list.Sum(x => x.Assists);
        Mvps = list.Sum(x => x.Mvps);
        OwnGoals = list.Sum(x => x.OwnGoals);
        CleanSheets = list.Sum(x => x.CleanSheets);
        HatTricks = list.Sum(x => x.HatTricks);
        Pokers = list.Sum(x => x.Pokers);
        FiveGoalGames = list.Sum(x => x.FiveGoalGames);
        GoalAndAssistGames = list.Sum(x => x.GoalAndAssistGames);
        ThreeAssistGames = list.Sum(x => x.ThreeAssistGames);
        CountRuns(list);
        ProjectedAt = DateTimeOffset.UtcNow;
    }

    private void CountRuns(IReadOnlyList<PlayerMatchStatContributionEntity> rows)
    {
        var unbeaten = 0;
        var wins = 0;
        var clean = 0;
        foreach (var row in rows)
        {
            unbeaten = row.Result == "L" ? 0 : unbeaten + 1;
            wins = row.Result == "W" ? wins + 1 : 0;
            clean = row.CleanSheets > 0 ? clean + 1 : 0;
            if (unbeaten == 5) UnbeatenFiveRuns++;
            if (unbeaten == 10) UnbeatenTenRuns++;
            if (wins == 5) FiveWinRuns++;
            if (clean == 3) ThreeCleanSheetRuns++;
        }
    }
}
