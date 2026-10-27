using BratnavaFC.Domain.Championships;

namespace BranavaFC.Tests;

public sealed class ChampionshipEngineWinnerStaysTests
{
    private static readonly Guid TeamA = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TeamB = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid TeamC = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid TeamD = Guid.Parse("00000000-0000-0000-0000-000000000004");

    [Fact]
    public void GetState_Win_KeepsWinnerAndSendsLoserToEndOfQueue()
    {
        var state = WinnerStaysEngine.GetState(
            new[] { TeamA, TeamB, TeamC, TeamD },
            new[] { new ChampionshipMatch(TeamA, TeamB, 1, 0) },
            new WinnerStaysOptions(0, WinnerStaysTieRule.BothLeave));

        Assert.Equal(new WinnerStaysNextGame(TeamA, TeamC), state.NextGame);
        Assert.Equal(new[] { TeamD, TeamB }, state.WaitingQueue);
        Assert.Equal(TeamA, state.ConsecutiveWinnerId);
        Assert.Equal(1, state.ConsecutiveWins);
    }

    [Fact]
    public void GetState_AfterTwoConsecutiveWins_ReplacesWinnerAndKeepsLastLoser()
    {
        var state = WinnerStaysEngine.GetState(
            new[] { TeamA, TeamB, TeamC, TeamD },
            new[]
            {
                new ChampionshipMatch(TeamA, TeamB, 1, 0),
                new ChampionshipMatch(TeamA, TeamC, 2, 0)
            },
            new WinnerStaysOptions(2, WinnerStaysTieRule.BothLeave));

        Assert.Equal(new WinnerStaysNextGame(TeamD, TeamC), state.NextGame);
        Assert.Equal(new[] { TeamB, TeamA }, state.WaitingQueue);
        Assert.Null(state.ConsecutiveWinnerId);
        Assert.Equal(0, state.ConsecutiveWins);
    }

    [Fact]
    public void GetState_LimitWithBothLeaving_FourTeams_SendsBothToQueueAndPullsNextTwo()
    {
        var state = WinnerStaysEngine.GetState(
            new[] { TeamA, TeamB, TeamC, TeamD },
            new[]
            {
                new ChampionshipMatch(TeamA, TeamB, 1, 0),
                new ChampionshipMatch(TeamA, TeamC, 2, 0)
            },
            new WinnerStaysOptions(2, WinnerStaysTieRule.LongestOnFieldLeaves, WinnerStaysLimitRule.BothLeave));

        Assert.Equal(new WinnerStaysNextGame(TeamD, TeamB), state.NextGame);
        Assert.Equal(new[] { TeamA, TeamC }, state.WaitingQueue);
        Assert.Null(state.ConsecutiveWinnerId);
    }

    [Fact]
    public void GetState_LimitWithBothLeaving_ThreeTeams_FallsBackToWinnerLeaving()
    {
        // Com 3 times só há 1 esperando: não dá para saírem os dois.
        var history = new[]
        {
            new ChampionshipMatch(TeamA, TeamB, 1, 0),
            new ChampionshipMatch(TeamA, TeamC, 2, 0)
        };

        var bothLeave = WinnerStaysEngine.GetState(new[] { TeamA, TeamB, TeamC }, history,
            new WinnerStaysOptions(2, WinnerStaysTieRule.LongestOnFieldLeaves, WinnerStaysLimitRule.BothLeave));
        var winnerLeaves = WinnerStaysEngine.GetState(new[] { TeamA, TeamB, TeamC }, history,
            new WinnerStaysOptions(2, WinnerStaysTieRule.LongestOnFieldLeaves, WinnerStaysLimitRule.WinnerLeaves));

        Assert.Equal(new WinnerStaysNextGame(TeamB, TeamC), bothLeave.NextGame);
        Assert.Equal(new[] { TeamA }, bothLeave.WaitingQueue);
        Assert.Equal(winnerLeaves.NextGame, bothLeave.NextGame);
        Assert.Equal(winnerLeaves.WaitingQueue, bothLeave.WaitingQueue);
    }

    [Fact]
    public void GetState_DrawWithBothLeaving_PullsFirstTwoWaitingTeams()
    {
        var state = WinnerStaysEngine.GetState(
            new[] { TeamA, TeamB, TeamC, TeamD },
            new[] { new ChampionshipMatch(TeamA, TeamB, 1, 1) },
            new WinnerStaysOptions(0, WinnerStaysTieRule.BothLeave));

        Assert.Equal(new WinnerStaysNextGame(TeamC, TeamD), state.NextGame);
        Assert.Equal(new[] { TeamA, TeamB }, state.WaitingQueue);
    }

    [Fact]
    public void GetState_DrawWithLongestLeaving_KeepsMostRecentTeam()
    {
        var state = WinnerStaysEngine.GetState(
            new[] { TeamA, TeamB, TeamC },
            new[] { new ChampionshipMatch(TeamA, TeamB, 0, 0) },
            new WinnerStaysOptions(0, WinnerStaysTieRule.LongestOnFieldLeaves));

        Assert.Equal(new WinnerStaysNextGame(TeamC, TeamB), state.NextGame);
        Assert.Equal(new[] { TeamA }, state.WaitingQueue);
    }

    [Fact]
    public void GetState_DrawWithPenalties_UsesExternalWinner()
    {
        var state = WinnerStaysEngine.GetState(
            new[] { TeamA, TeamB, TeamC },
            new[] { new ChampionshipMatch(TeamA, TeamB, 2, 2, TeamB) },
            new WinnerStaysOptions(0, WinnerStaysTieRule.PenaltyShootout));

        Assert.Equal(new WinnerStaysNextGame(TeamC, TeamB), state.NextGame);
        Assert.Equal(TeamB, state.ConsecutiveWinnerId);
    }

    [Fact]
    public void GetState_HistoryOutsideExpectedRotation_Throws()
    {
        var action = () => WinnerStaysEngine.GetState(
            new[] { TeamA, TeamB, TeamC },
            new[] { new ChampionshipMatch(TeamA, TeamC, 1, 0) },
            new WinnerStaysOptions(0, WinnerStaysTieRule.BothLeave));

        var exception = Assert.Throws<InvalidOperationException>(action);
        Assert.Contains("nao corresponde", exception.Message);
    }
}
