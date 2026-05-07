using BratnavaFC.Domain.Entities;
using FluentAssertions;

namespace BranavaFC.Tests;

public class GoalEntityTests
{
    private static GoalEntity Make(
        Guid? matchId  = null,
        Guid? groupId  = null,
        Guid? scorer   = null,
        Guid? assist   = null,
        int?  time     = null,
        bool  ownGoal  = false)
    {
        return new GoalEntity(
            matchId  ?? Guid.NewGuid(),
            groupId  ?? Guid.NewGuid(),
            scorer   ?? Guid.NewGuid(),
            assist,
            time,
            ownGoal);
    }

    // ── Construtor ────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_HappyPath_ShouldSetProperties()
    {
        var matchId  = Guid.NewGuid();
        var groupId  = Guid.NewGuid();
        var scorer   = Guid.NewGuid();
        var assist   = Guid.NewGuid();

        var sut = new GoalEntity(matchId, groupId, scorer, assist, 90, false);

        sut.MatchId.Should().Be(matchId);
        sut.GroupId.Should().Be(groupId);
        sut.ScorerMatchPlayerId.Should().Be(scorer);
        sut.AssistMatchPlayerId.Should().Be(assist);
        sut.TimeSeconds.Should().Be(90);
        sut.IsOwnGoal.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithoutAssist_ShouldHaveNullAssist()
    {
        var sut = Make();

        sut.AssistMatchPlayerId.Should().BeNull();
    }

    [Fact]
    public void Constructor_OwnGoal_ShouldSetFlag()
    {
        var sut = Make(ownGoal: true);

        sut.IsOwnGoal.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithoutTime_ShouldHaveNullTime()
    {
        var sut = Make(time: null);

        sut.TimeSeconds.Should().BeNull();
    }

    [Fact]
    public void Constructor_EmptyMatchId_ShouldThrow()
    {
        var act = () => Make(matchId: Guid.Empty);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_EmptyGroupId_ShouldThrow()
    {
        var act = () => Make(groupId: Guid.Empty);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_EmptyScorer_ShouldThrow()
    {
        var act = () => Make(scorer: Guid.Empty);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_NegativeTime_ShouldThrow()
    {
        var act = () => Make(time: -1);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_ZeroTime_ShouldBeAllowed()
    {
        var sut = Make(time: 0);

        sut.TimeSeconds.Should().Be(0);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_ShouldChangeAllFields()
    {
        var sut        = Make();
        var newScorer  = Guid.NewGuid();
        var newAssist  = Guid.NewGuid();

        sut.Update(newScorer, newAssist, 45, true);

        sut.ScorerMatchPlayerId.Should().Be(newScorer);
        sut.AssistMatchPlayerId.Should().Be(newAssist);
        sut.TimeSeconds.Should().Be(45);
        sut.IsOwnGoal.Should().BeTrue();
    }

    [Fact]
    public void Update_ClearAssist_ShouldSetNullAssist()
    {
        var assist = Guid.NewGuid();
        var sut    = Make(assist: assist);

        sut.Update(Guid.NewGuid(), null, null, false);

        sut.AssistMatchPlayerId.Should().BeNull();
    }

    [Fact]
    public void Update_ClearTime_ShouldSetNullTime()
    {
        var sut = Make(time: 60);

        sut.Update(Guid.NewGuid(), null, null, false);

        sut.TimeSeconds.Should().BeNull();
    }

    [Fact]
    public void Update_EmptyScorer_ShouldThrow()
    {
        var sut = Make();

        var act = () => sut.Update(Guid.Empty, null, null, false);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Update_NegativeTime_ShouldThrow()
    {
        var sut = Make();

        var act = () => sut.Update(Guid.NewGuid(), null, -5, false);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Update_OwnGoalFlag_CanBeToggled()
    {
        var sut = Make(ownGoal: false);

        sut.Update(Guid.NewGuid(), null, null, true);
        sut.IsOwnGoal.Should().BeTrue();

        sut.Update(Guid.NewGuid(), null, null, false);
        sut.IsOwnGoal.Should().BeFalse();
    }
}
