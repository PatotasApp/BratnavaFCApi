using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;

namespace BranavaFC.Tests;

// ── UserBetBalanceEntity ──────────────────────────────────────────────────────

public class UserBetBalanceEntityTests
{
    private static UserBetBalanceEntity Make() =>
        new(Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Constructor_ShouldStartWithZeroBalance()
    {
        var sut = Make();

        sut.Balance.Should().Be(0);
        sut.TotalBets.Should().Be(0);
        sut.TotalCorrect.Should().Be(0);
    }

    [Fact]
    public void ApplyDelta_Positive_ShouldIncreaseBalance()
    {
        var sut = Make();

        sut.ApplyDelta(150);

        sut.Balance.Should().Be(150);
    }

    [Fact]
    public void ApplyDelta_Negative_ShouldDecreaseBalance()
    {
        var sut = Make();
        sut.ApplyDelta(100);

        sut.ApplyDelta(-40);

        sut.Balance.Should().Be(60);
    }

    [Fact]
    public void ApplyDelta_CanGoBelowZero()
    {
        var sut = Make();

        sut.ApplyDelta(-200);

        sut.Balance.Should().Be(-200);
    }

    [Fact]
    public void ApplyDelta_Zero_ShouldNotChangeBalance()
    {
        var sut = Make();
        sut.ApplyDelta(100);

        sut.ApplyDelta(0);

        sut.Balance.Should().Be(100);
    }

    [Fact]
    public void ApplyDelta_MultipleTimes_ShouldAccumulate()
    {
        var sut = Make();

        sut.ApplyDelta(100);
        sut.ApplyDelta(50);
        sut.ApplyDelta(-25);

        sut.Balance.Should().Be(125);
    }

    [Fact]
    public void RecordBetResult_ShouldIncrementTotalBets()
    {
        var sut = Make();

        sut.RecordBetResult(2);

        sut.TotalBets.Should().Be(1);
        sut.TotalCorrect.Should().Be(2);
    }

    [Fact]
    public void RecordBetResult_MultipleCalls_ShouldAccumulate()
    {
        var sut = Make();

        sut.RecordBetResult(3);
        sut.RecordBetResult(1);

        sut.TotalBets.Should().Be(2);
        sut.TotalCorrect.Should().Be(4);
    }

    [Fact]
    public void RecordBetResult_ZeroCorrect_ShouldIncrementOnlyTotalBets()
    {
        var sut = Make();

        sut.RecordBetResult(0);

        sut.TotalBets.Should().Be(1);
        sut.TotalCorrect.Should().Be(0);
    }

    [Fact]
    public void ForceSetBalance_ShouldReplaceBalance()
    {
        var sut = Make();
        sut.ApplyDelta(999);

        sut.ForceSetBalance(125);

        sut.Balance.Should().Be(125);
    }

    [Fact]
    public void ForceSetBalance_NegativeValue_ShouldSetNegative()
    {
        var sut = Make();

        sut.ForceSetBalance(-50);

        sut.Balance.Should().Be(-50);
    }

    [Fact]
    public void ReverseBetResult_ShouldDeductDeltaAndDecrementCounters()
    {
        var sut = Make();
        sut.ApplyDelta(150);
        sut.RecordBetResult(2);

        sut.ReverseBetResult(2, 150);

        sut.Balance.Should().Be(0);
        sut.TotalBets.Should().Be(0);
        sut.TotalCorrect.Should().Be(0);
    }

    [Fact]
    public void ReverseBetResult_PartialCorrect_ShouldDeductCorrectly()
    {
        var sut = Make();
        sut.ApplyDelta(200);
        sut.RecordBetResult(3);

        sut.ReverseBetResult(1, 100);

        sut.Balance.Should().Be(100);
        sut.TotalBets.Should().Be(0);
        sut.TotalCorrect.Should().Be(2);
    }

    [Fact]
    public void GroupId_And_UserId_ShouldBePreserved()
    {
        var groupId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        var sut = new UserBetBalanceEntity(groupId, userId);

        sut.GroupId.Should().Be(groupId);
        sut.UserId.Should().Be(userId);
    }
}

// ── MatchBetEntity ────────────────────────────────────────────────────────────

public class MatchBetEntityTests
{
    private static MatchBetEntity Make() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Constructor_ShouldStartUnresolved()
    {
        var sut = Make();

        sut.IsResolved.Should().BeFalse();
        sut.Selections.Should().BeEmpty();
    }

    [Fact]
    public void MarkResolved_ShouldSetIsResolvedTrue()
    {
        var sut = Make();

        sut.MarkResolved();

        sut.IsResolved.Should().BeTrue();
    }

    [Fact]
    public void Unresolve_ShouldSetIsResolvedFalse()
    {
        var sut = Make();
        sut.MarkResolved();

        sut.Unresolve();

        sut.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void ReplaceSelections_ShouldReplaceExistingList()
    {
        var sut     = Make();
        var betId   = sut.Id;
        var first   = new MatchBetSelectionEntity(betId, BetCategory.WinningTeam, "TeamA", 50);
        var second  = new MatchBetSelectionEntity(betId, BetCategory.FinalScore,  "2:1",   100);

        sut.ReplaceSelections(new List<MatchBetSelectionEntity> { first });
        sut.ReplaceSelections(new List<MatchBetSelectionEntity> { second });

        sut.Selections.Should().HaveCount(1);
        sut.Selections[0].Category.Should().Be(BetCategory.FinalScore);
    }

    [Fact]
    public void ReplaceSelections_WithMultiple_ShouldContainAll()
    {
        var sut   = Make();
        var betId = sut.Id;
        var sels  = new List<MatchBetSelectionEntity>
        {
            new(betId, BetCategory.WinningTeam,   "TeamA", 50),
            new(betId, BetCategory.FinalScore,    "2:1",   100),
            new(betId, BetCategory.PlayerGoals,   $"{Guid.NewGuid()}|2", 50),
        };

        sut.ReplaceSelections(sels);

        sut.Selections.Should().HaveCount(3);
    }
}

// ── MatchBetSelectionEntity ───────────────────────────────────────────────────

public class MatchBetSelectionEntityTests
{
    private static MatchBetSelectionEntity Make(BetCategory cat = BetCategory.WinningTeam, string val = "TeamA", int wager = 50)
        => new(Guid.NewGuid(), cat, val, wager);

    [Fact]
    public void Constructor_ShouldSetPropertiesAndLeaveResolutionNull()
    {
        var sut = Make(BetCategory.FinalScore, "2:1", 80);

        sut.Category.Should().Be(BetCategory.FinalScore);
        sut.PredictedValue.Should().Be("2:1");
        sut.FichasWagered.Should().Be(80);
        sut.FichasEarned.Should().BeNull();
        sut.IsCorrect.Should().BeNull();
        sut.IsPartialCredit.Should().BeNull();
        sut.ActualValue.Should().BeNull();
    }

    [Fact]
    public void Resolve_Correct_ShouldSetAllFields()
    {
        var sut = Make();

        sut.Resolve(50, true, false, "TeamA");

        sut.FichasEarned.Should().Be(50);
        sut.IsCorrect.Should().BeTrue();
        sut.IsPartialCredit.Should().BeFalse();
        sut.ActualValue.Should().Be("TeamA");
    }

    [Fact]
    public void Resolve_Wrong_ShouldSetNegativeEarnings()
    {
        var sut = Make(wager: 100);

        sut.Resolve(-50, false, false, "TeamB");

        sut.FichasEarned.Should().Be(-50);
        sut.IsCorrect.Should().BeFalse();
        sut.IsPartialCredit.Should().BeFalse();
    }

    [Fact]
    public void Resolve_Partial_ShouldSetPartialCredit()
    {
        var sut = Make(BetCategory.FinalScore, "2:1", 60);

        sut.Resolve(0, false, true, "2:0");

        sut.FichasEarned.Should().Be(0);
        sut.IsCorrect.Should().BeFalse();
        sut.IsPartialCredit.Should().BeTrue();
    }

    [Fact]
    public void Unresolve_ShouldClearAllResolutionFields()
    {
        var sut = Make();
        sut.Resolve(50, true, false, "TeamA");

        sut.Unresolve();

        sut.FichasEarned.Should().BeNull();
        sut.IsCorrect.Should().BeNull();
        sut.IsPartialCredit.Should().BeNull();
        sut.ActualValue.Should().BeNull();
    }
}
