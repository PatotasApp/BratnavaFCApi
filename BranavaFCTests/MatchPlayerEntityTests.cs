using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;

namespace BranavaFC.Tests;

public class MatchPlayerEntityTests
{
    [Fact]
    public void Ctor_WithEmptyPlayerId_ShouldThrow()
    {
        // Arrange + Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new MatchPlayerEntity(Guid.Empty));
        Assert.Equal("PlayerId e obrigatorio.", ex.Message);
    }

    [Theory]
    [InlineData((short)-1)]
    [InlineData((short)3)]
    public void SetTeam_WithInvalidValue_ShouldThrow(short team)
    {
        // Arrange
        var mp = new MatchPlayerEntity(Guid.NewGuid());

        // Act + Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => mp.SetTeam(team));
    }

    [Fact]
    public void AssignGroup_WithEmptyGroupId_ShouldThrow()
    {
        // Arrange
        var mp = new MatchPlayerEntity(Guid.NewGuid());

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => mp.AssignGroup(Guid.Empty));
        Assert.Equal("GroupId e obrigatorio.", ex.Message);
    }

    [Fact]
    public void AddReceivedVote_ShouldNotDuplicate()
    {
        // Arrange
        var mp = new MatchPlayerEntity(Guid.NewGuid());
        var vote = new VoteEntity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // Act
        mp.AddReceivedVote(vote);
        mp.AddReceivedVote(vote);

        // Assert
        Assert.Single(mp.ReceivedVotes);
    }

    [Fact]
    public void RemoveReceivedVote_ShouldRemoveIfExists()
    {
        // Arrange
        var mp = new MatchPlayerEntity(Guid.NewGuid());
        var vote = new VoteEntity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        mp.AddReceivedVote(vote);

        // Act
        mp.RemoveReceivedVote(vote.Id);

        // Assert
        Assert.Empty(mp.ReceivedVotes);
    }

    [Fact]
    public void SetVotedFor_AndClear_ShouldWork()
    {
        // Arrange
        var mp = new MatchPlayerEntity(Guid.NewGuid());
        var voted = Guid.NewGuid();

        // Act
        mp.SetVotedFor(voted);
        mp.ClearVotedFor();

        // Assert
        Assert.Null(mp.VotedForId);
    }

    [Fact]
    public void AutoRejectByAbsence_ShouldRejectAndLinkAbsence()
    {
        // Arrange
        var mp = new MatchPlayerEntity(Guid.NewGuid());
        var absenceId = Guid.NewGuid();

        // Act
        mp.AutoRejectByAbsence(absenceId);

        // Assert
        Assert.Equal(InviteResponse.Rejected, mp.InviteResponse);
        Assert.Equal(absenceId, mp.AutoRejectedByAbsenceId);
    }

    [Fact]
    public void AutoRejectByAbsence_WithEmptyGuid_ShouldThrow()
    {
        // Arrange
        var mp = new MatchPlayerEntity(Guid.NewGuid());

        // Act + Assert
        Assert.Throws<InvalidOperationException>(() => mp.AutoRejectByAbsence(Guid.Empty));
    }

    [Fact]
    public void ClearAutoRejection_ShouldResetInviteToNoneAndUnlinkAbsence()
    {
        // Arrange
        var mp = new MatchPlayerEntity(Guid.NewGuid());
        mp.AutoRejectByAbsence(Guid.NewGuid());

        // Act
        mp.ClearAutoRejection();

        // Assert
        Assert.Equal(InviteResponse.None, mp.InviteResponse);
        Assert.Null(mp.AutoRejectedByAbsenceId);
    }
}
