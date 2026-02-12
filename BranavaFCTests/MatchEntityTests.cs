using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;

namespace BranavaFC.Tests;

public class MatchEntityTests
{
    private static (MatchEntity match, PlayerEntity p1, PlayerEntity p2, MatchPlayerEntity mp1, MatchPlayerEntity mp2) CreateMatchWithTwoPlayers()
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Boca Jrs");

        var p1 = new PlayerEntity("A", Guid.NewGuid(), groupId, 0, false, Status.Active);
        var p2 = new PlayerEntity("B", Guid.NewGuid(), groupId, 0, false, Status.Active);

        var mp1 = new MatchPlayerEntity(Guid.NewGuid());
        var mp2 = new MatchPlayerEntity(Guid.NewGuid());

        match.AddPlayer(mp1, p1);
        match.AddPlayer(mp2, p2);

        mp1.InviteResponse = InviteResponse.Accepted;
        mp2.InviteResponse = InviteResponse.Accepted;

        return (match, p1, p2, mp1, mp2);
    }

    [Fact]
    public void Ctor_WithEmptyGroupId_ShouldThrow()
    {
        // Arrange + Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new MatchEntity(Guid.Empty, DateTime.UtcNow, "X"));

        Assert.Equal("GroupId e obrigatorio.", ex.Message);
    }

    [Fact]
    public void UpdateDetails_WhenFinalized_ShouldThrow()
    {
        // Arrange
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers();

        match.AssignTeams(
            teamAPlayerIds: new[] { match.Players[0].PlayerId },
            teamBPlayerIds: new[] { match.Players[1].PlayerId });

        match.Start();
        match.End();
        match.SetScore(1, 0);
        match.FinalizeByVotes();

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.UpdateDetails(match.GroupId, DateTime.UtcNow, "Novo", match.Id, match.Id));

        Assert.Equal("Partida ja Finalizada. Nao e possivel atualizar seus dados.", ex.Message);
    }

    [Fact]
    public void AddPlayer_ShouldIgnoreDuplicatesByPlayerId()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Local");
        var p1 = new PlayerEntity("A", Guid.NewGuid(), groupId, 0, false, Status.Active);

        var mp1 = new MatchPlayerEntity(Guid.NewGuid());
        match.AddPlayer(mp1, p1);

        var mp2 = new MatchPlayerEntity(Guid.NewGuid());

        // Act (mesmo player novamente)
        match.AddPlayer(mp2, p1);

        // Assert
        Assert.Single(match.Players);
    }

    [Fact]
    public void AssignTeams_WithNotAcceptedInvite_ShouldThrow()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Local");

        var p1 = new PlayerEntity("A", Guid.NewGuid(), groupId, 0, false, Status.Active);
        var p2 = new PlayerEntity("B", Guid.NewGuid(), groupId, 0, false, Status.Active);

        var mp1 = new MatchPlayerEntity(Guid.NewGuid());
        var mp2 = new MatchPlayerEntity(Guid.NewGuid());
        match.AddPlayer(mp1, p1);
        match.AddPlayer(mp2, p2);

        mp1.InviteResponse = InviteResponse.Accepted;
        mp2.InviteResponse = InviteResponse.None; // nao aceitou

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.AssignTeams(
                new[] { p1.Id },
                new[] { p2.Id }));

        Assert.Equal("Ha jogadores que ainda nao aceitaram o convite.", ex.Message);
    }

    [Fact]
    public void Start_WithoutTeams_ShouldThrow()
    {
        // Arrange
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers();
        // times ainda 0/0

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => match.Start());
        Assert.Equal("Nao e possivel iniciar a partida sem os times estarem definidos.", ex.Message);
    }

    [Fact]
    public void FullFlow_Start_End_SetScore_Finalize_ShouldWork()
    {
        // Arrange
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers();

        match.AssignTeams(
            teamAPlayerIds: new[] { mp1.PlayerId },
            teamBPlayerIds: new[] { mp2.PlayerId });

        // Act
        match.Start();
        match.End();
        match.SetScore(2, 1);
        match.FinalizeByVotes();

        // Assert
        Assert.Equal(MatchStatus.Finalized, match.Status);
        Assert.Equal(2, match.TeamAGoals);
        Assert.Equal(1, match.TeamBGoals);
    }

    [Fact]
    public void CreateVote_WhenEnded_ShouldSetVotedFor_AndAddReceivedVote_AndPreventDoubleVote()
    {
        // Arrange
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers();

        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        match.Start();
        match.End();
        match.SetScore(1, 1);

        // Act
        var vote = match.CreateVote(mp1.Id, mp2.Id);
        match.Votes.Add(vote); // normalmente e o servico/repo que adiciona

        // Assert
        Assert.Equal(mp2.Id, mp1.VotedForId);
        Assert.Contains(mp2.ReceivedVotes, v => v.Id == vote.Id);

        // Act + Assert (mesmo voter tentando votar de novo)
        var ex = Assert.Throws<InvalidOperationException>(() => match.CreateVote(mp1.Id, mp2.Id));
        Assert.Equal("Esse jogador ja votou.", ex.Message);
    }

    [Fact]
    public void GetComputedMvp_ShouldReturnPlayerWithMostVotes()
    {
        // Arrange
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers();

        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        match.Start();
        match.End();
        match.SetScore(1, 0);

        var v1 = match.CreateVote(mp1.Id, mp2.Id);
        match.Votes.Add(v1);

        // Act
        var mvp = match.GetComputedMvp();

        // Assert
        Assert.NotNull(mvp);
        Assert.Equal(mp2.Id, mvp!.Id);
    }

    [Fact]
    public void SwapPlayers_ShouldSwapTeams()
    {
        // Arrange
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers();

        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        Assert.Equal((short)1, mp1.Team);
        Assert.Equal((short)2, mp2.Team);

        // Act
        match.SwapPlayers(mp1.Id, mp2.Id);

        // Assert
        Assert.Equal((short)2, mp1.Team);
        Assert.Equal((short)1, mp2.Team);
    }

    [Fact]
    public void SetTeamColors_WithSameColor_ShouldThrow()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Local");
        var colorId = Guid.NewGuid();

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => match.SetTeamColors(colorId, colorId));
        Assert.Equal("Os dois times nao podem possuir a mesma cor.", ex.Message);
    }

    [Fact]
    public void SetTeamColorsRandomly_WithoutColors_ShouldThrow()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Local");

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() => match.SetTeamColorsRandomly(Array.Empty<TeamColorEntity>()));
        Assert.Equal("Nao ha cores cadastradas para sortear.", ex.Message);
    }
}
