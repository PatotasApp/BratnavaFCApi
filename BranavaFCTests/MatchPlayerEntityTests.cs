using BratnavaFC.Domain.Entities;

namespace BranavaFC.Tests;

public class MatchPlayerEntityTests
{
    [Fact]
    public void Ctor_WithEmptyPlayerId_ShouldThrow()
    {
        // Arrange + Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new MatchPlayerEntity(Guid.Empty));
        Assert.Equal("PlayerId é obrigatório.", ex.Message);
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
        Assert.Equal("GroupId é obrigatório.", ex.Message);
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
}
