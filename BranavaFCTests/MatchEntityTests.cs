using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Xunit;

namespace BranavaFC.Tests;

public class MatchEntityTests
{
    private static (MatchEntity match, PlayerEntity p1, PlayerEntity p2, MatchPlayerEntity mp1, MatchPlayerEntity mp2) CreateMatchWithTwoPlayers()
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Boca Jrs");

        var p1 = new PlayerEntity("A", Guid.NewGuid(), groupId, 0, false, Status.Active);
        var p2 = new PlayerEntity("B", Guid.NewGuid(), groupId, 0, false, Status.Active);

        // ✅ importante: criar MatchPlayerEntity com o PlayerId correto (consistente)
        var mp1 = new MatchPlayerEntity(p1.Id);
        var mp2 = new MatchPlayerEntity(p2.Id);

        match.AddPlayer(mp1, p1);
        match.AddPlayer(mp2, p2);

        mp1.InviteResponse = InviteResponse.Accepted;
        mp2.InviteResponse = InviteResponse.Accepted;

        return (match, p1, p2, mp1, mp2);
    }

    private static (MatchEntity match, PlayerEntity a1, PlayerEntity a2, PlayerEntity b1) CreateMatchWithThreePlayers_TwoInA_OneInB(bool start = true, bool end = false)
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Boca Jrs");

        var a1 = new PlayerEntity("A1", Guid.NewGuid(), groupId, 0, false, Status.Active);
        var a2 = new PlayerEntity("A2", Guid.NewGuid(), groupId, 0, false, Status.Active);
        var b1 = new PlayerEntity("B1", Guid.NewGuid(), groupId, 0, false, Status.Active);

        var mp1 = new MatchPlayerEntity(a1.Id);
        var mp2 = new MatchPlayerEntity(a2.Id);
        var mp3 = new MatchPlayerEntity(b1.Id);

        match.AddPlayer(mp1, a1);
        match.AddPlayer(mp2, a2);
        match.AddPlayer(mp3, b1);

        mp1.InviteResponse = InviteResponse.Accepted;
        mp2.InviteResponse = InviteResponse.Accepted;
        mp3.InviteResponse = InviteResponse.Accepted;

        match.AssignTeams(
            teamAPlayerIds: new[] { a1.Id, a2.Id },
            teamBPlayerIds: new[] { b1.Id });

        if (start) match.Start();
        if (end) match.End();

        return (match, a1, a2, b1);
    }

    private static (MatchEntity match, PlayerEntity a1, PlayerEntity a2, PlayerEntity b1, PlayerEntity b2) CreateMatchWithFourPlayers_TwoInA_TwoInB(bool start = true, bool end = false)
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Boca Jrs");

        var a1 = new PlayerEntity("A1", Guid.NewGuid(), groupId, 0, false, Status.Active);
        var a2 = new PlayerEntity("A2", Guid.NewGuid(), groupId, 0, false, Status.Active);
        var b1 = new PlayerEntity("B1", Guid.NewGuid(), groupId, 0, false, Status.Active);
        var b2 = new PlayerEntity("B2", Guid.NewGuid(), groupId, 0, false, Status.Active);

        var mp1 = new MatchPlayerEntity(a1.Id);
        var mp2 = new MatchPlayerEntity(a2.Id);
        var mp3 = new MatchPlayerEntity(b1.Id);
        var mp4 = new MatchPlayerEntity(b2.Id);

        match.AddPlayer(mp1, a1);
        match.AddPlayer(mp2, a2);
        match.AddPlayer(mp3, b1);
        match.AddPlayer(mp4, b2);

        mp1.InviteResponse = InviteResponse.Accepted;
        mp2.InviteResponse = InviteResponse.Accepted;
        mp3.InviteResponse = InviteResponse.Accepted;
        mp4.InviteResponse = InviteResponse.Accepted;

        match.AssignTeams(
            teamAPlayerIds: new[] { a1.Id, a2.Id },
            teamBPlayerIds: new[] { b1.Id, b2.Id });

        if (start) match.Start();
        if (end) match.End();

        return (match, a1, a2, b1, b2);
    }

    [Fact]
    public void Ctor_WithEmptyGroupId_ShouldThrow()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new MatchEntity(Guid.Empty, DateTime.UtcNow, "X"));

        Assert.Equal("GroupId e obrigatorio.", ex.Message);
    }

    [Fact]
    public void UpdateDetails_WhenFinalized_ShouldThrow()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers();

        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        match.Start();
        match.End();
        match.SetScore(1, 0);
        match.FinalizeByVotes();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.UpdateDetails(match.GroupId, DateTime.UtcNow, "Novo", match.Id, match.Id));

        Assert.Equal("Partida ja Finalizada. Nao e possivel atualizar seus dados.", ex.Message);
    }

    [Fact]
    public void AddPlayer_ShouldIgnoreDuplicatesByPlayerId()
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Local");
        var p1 = new PlayerEntity("A", Guid.NewGuid(), groupId, 0, false, Status.Active);

        var mp1 = new MatchPlayerEntity(p1.Id);
        match.AddPlayer(mp1, p1);

        var mp2 = new MatchPlayerEntity(p1.Id);

        match.AddPlayer(mp2, p1);

        Assert.Single(match.Players);
    }

    [Fact]
    public void AssignTeams_WithNotAcceptedInvite_ShouldThrow()
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Local");

        var p1 = new PlayerEntity("A", Guid.NewGuid(), groupId, 0, false, Status.Active);
        var p2 = new PlayerEntity("B", Guid.NewGuid(), groupId, 0, false, Status.Active);

        var mp1 = new MatchPlayerEntity(p1.Id);
        var mp2 = new MatchPlayerEntity(p2.Id);

        match.AddPlayer(mp1, p1);
        match.AddPlayer(mp2, p2);

        mp1.InviteResponse = InviteResponse.Accepted;
        mp2.InviteResponse = InviteResponse.None;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.AssignTeams(new[] { p1.Id }, new[] { p2.Id }));

        Assert.Equal("Ha jogadores que ainda nao aceitaram o convite.", ex.Message);
    }

    [Fact]
    public void Start_WithoutTeams_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers();

        var ex = Assert.Throws<InvalidOperationException>(() => match.Start());
        Assert.Equal("Nao e possivel iniciar a partida sem os times estarem definidos.", ex.Message);
    }

    [Fact]
    public void FullFlow_Start_End_SetScore_Finalize_ShouldWork()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers();

        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        match.Start();
        match.End();
        match.SetScore(2, 1);
        match.FinalizeByVotes();

        Assert.Equal(MatchStatus.Finalized, match.Status);
        Assert.Equal(2, match.TeamAGoals);
        Assert.Equal(1, match.TeamBGoals);
    }

    [Fact]
    public void CreateVote_WhenEnded_ShouldSetVotedFor_AndAddReceivedVote_AndPreventDoubleVote()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers();

        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        match.Start();
        match.End();
        match.SetScore(1, 1);

        var vote = match.CreateVote(mp1.Id, mp2.Id);
        match.Votes.Add(vote);

        Assert.Equal(mp2.Id, mp1.VotedForId);
        Assert.Contains(mp2.ReceivedVotes, v => v.Id == vote.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => match.CreateVote(mp1.Id, mp2.Id));
        Assert.Equal("Esse jogador ja votou.", ex.Message);
    }

    [Fact]
    public void GetComputedMvp_ShouldReturnPlayerWithMostVotes()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers();

        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        match.Start();
        match.End();
        match.SetScore(1, 0);

        var v1 = match.CreateVote(mp1.Id, mp2.Id);
        match.Votes.Add(v1);

        var mvp = match.GetComputedMvp();

        Assert.NotNull(mvp);
        Assert.Equal(mp2.Id, mvp!.Id);
    }

    [Fact]
    public void SwapPlayers_ShouldSwapTeams()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers();

        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        Assert.Equal((short)1, mp1.Team);
        Assert.Equal((short)2, mp2.Team);

        match.SwapPlayers(mp1.Id, mp2.Id);

        Assert.Equal((short)2, mp1.Team);
        Assert.Equal((short)1, mp2.Team);
    }

    [Fact]
    public void SetTeamColors_WithSameColor_ShouldThrow()
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Local");
        var colorId = Guid.NewGuid();

        var ex = Assert.Throws<InvalidOperationException>(() => match.SetTeamColors(colorId, colorId));
        Assert.Equal("Os dois times nao podem possuir a mesma cor.", ex.Message);
    }

    [Fact]
    public void SetTeamColorsRandomly_WithoutColors_ShouldThrow()
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Local");

        var ex = Assert.Throws<InvalidOperationException>(() => match.SetTeamColorsRandomly(Array.Empty<TeamColorEntity>()));
        Assert.Equal("Nao ha cores cadastradas para sortear.", ex.Message);
    }

    // =========================
    // ✅ NOVA FEATURE: Goals
    // =========================

    [Fact]
    public void AddGoal_WhenNotStartedOrEnded_ShouldThrow()
    {
        var (match, p1, _, mp1, mp2) = CreateMatchWithTwoPlayers();

        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        // status ainda Created

        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.AddGoal(p1.Id, assistPlayerId: null, timeSeconds: 10));

        Assert.Equal("So e possivel registrar gols quando a partida esta Iniciada ou Encerrada.", ex.Message);
    }

    [Fact]
    public void AddGoal_WhenScorerNotInMatch_ShouldThrow()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers();
        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        match.Start();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.AddGoal(Guid.NewGuid(), assistPlayerId: null, timeSeconds: 10));

        Assert.Equal("O jogador do gol nao pertence a esta partida.", ex.Message);
    }

    [Fact]
    public void AddGoal_WhenScorerUnassigned_ShouldThrow()
    {
        var (match, p1, _, mp1, mp2) = CreateMatchWithTwoPlayers();

        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        match.Start();

        // ✅ força o scorer ficar sem time
        var scorerMp = match.Players.First(x => x.PlayerId == p1.Id);
        scorerMp.SetTeam(0);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.AddGoal(p1.Id, assistPlayerId: null, timeSeconds: 10));

        Assert.Equal("Nao e possivel registrar gol de jogador nao atribuido a um time.", ex.Message);
    }

    [Fact]
    public void AddGoal_WhenAssistNotInMatch_ShouldThrow()
    {
        var (match, p1, _, mp1, mp2) = CreateMatchWithTwoPlayers();
        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        match.Start();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.AddGoal(p1.Id, assistPlayerId: Guid.NewGuid(), timeSeconds: 10));

        Assert.Equal("O jogador da assistencia nao pertence a esta partida.", ex.Message);
    }

    [Fact]
    public void AddGoal_WhenAssistIsScorer_ShouldThrow()
    {
        var (match, p1, _, mp1, mp2) = CreateMatchWithTwoPlayers();
        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        match.Start();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.AddGoal(p1.Id, assistPlayerId: p1.Id, timeSeconds: 10));

        Assert.Equal("Assistente nao pode ser o mesmo jogador do gol.", ex.Message);
    }

    [Fact]
    public void AddGoal_WhenAssistFromOtherTeam_ShouldThrow()
    {
        var (match, p1, p2, mp1, mp2) = CreateMatchWithTwoPlayers();

        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        match.Start();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.AddGoal(p1.Id, assistPlayerId: p2.Id, timeSeconds: 10));

        Assert.Equal("A assistencia so pode ser de um jogador do mesmo time do autor do gol.", ex.Message);
    }

    [Fact]
    public void AddGoal_WhenTimeNegative_ShouldThrow()
    {
        var (match, a1, _, _) = CreateMatchWithThreePlayers_TwoInA_OneInB();
        var ex = Assert.Throws<InvalidOperationException>(() => match.AddGoal(a1.Id, null, -1));
        Assert.Equal("Tempo do gol nao pode ser negativo.", ex.Message);
    }

    [Fact]
    public void AddGoal_WhenTimeNull_ShouldWork()
    {
        var (match, a1, _, _) = CreateMatchWithThreePlayers_TwoInA_OneInB();

        match.AddGoal(a1.Id, null, null);

        Assert.Single(match.Goals);
        Assert.Equal(1, match.TeamAGoals);
        Assert.Equal(0, match.TeamBGoals);
        Assert.Null(match.Goals[0].TimeSeconds);
    }

    [Fact]
    public void AddGoal_WhenStatusEnded_ShouldAllowRegisterGoal()
    {
        var (match, a1, _, _) = CreateMatchWithThreePlayers_TwoInA_OneInB(start: true, end: true);

        match.AddGoal(a1.Id, null, 10);

        Assert.Single(match.Goals);
        Assert.Equal(1, match.TeamAGoals);
        Assert.Equal(0, match.TeamBGoals);
    }

    [Fact]
    public void AddGoal_WhenAssistUnassigned_ShouldThrow()
    {
        var (match, a1, a2, _) = CreateMatchWithThreePlayers_TwoInA_OneInB();
        var assistMp = match.Players.First(p => p.PlayerId == a2.Id);
        assistMp.SetTeam(0);

        var ex = Assert.Throws<InvalidOperationException>(() => match.AddGoal(a1.Id, a2.Id, 10));
        Assert.Equal("Nao e possivel registrar assistencia de jogador nao atribuido a um time.", ex.Message);
    }

    [Fact]
    public void AddGoal_WhenValidWithAssistSameTeam_ShouldAddGoal_AndRecalculateScore()
    {
        var (match, a1, a2, _) = CreateMatchWithThreePlayers_TwoInA_OneInB();

        match.AddGoal(
            scorerPlayerId: a1.Id,
            assistPlayerId: a2.Id,
            timeSeconds: 12 * 60 + 34);

        Assert.Single(match.Goals);

        var g = match.Goals[0];
        Assert.Equal(a1.Id, g.ScorerPlayerId);
        Assert.Equal(a2.Id, g.AssistPlayerId);
        Assert.Equal(12 * 60 + 34, g.TimeSeconds);

        Assert.Equal(1, match.TeamAGoals);
        Assert.Equal(0, match.TeamBGoals);
    }

    [Fact]
    public void RemoveGoal_WhenNotFinalized_ShouldRemoveAndRecalculateScore()
    {
        var (match, p1, _, mp1, mp2) = CreateMatchWithTwoPlayers();
        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        match.Start();

        match.AddGoal(p1.Id, assistPlayerId: null, timeSeconds: 10);
        var goalId = match.Goals[0].Id;

        match.RemoveGoal(goalId);

        Assert.Empty(match.Goals);
        Assert.Equal(0, match.TeamAGoals);
        Assert.Equal(0, match.TeamBGoals);
    }

    [Fact]
    public void RemoveGoal_WhenGoalNotFound_ShouldNotThrow_AndNotChangeScore()
    {
        var (match, a1, _, _) = CreateMatchWithThreePlayers_TwoInA_OneInB();

        match.AddGoal(a1.Id, null, 10);
        Assert.Equal(1, match.TeamAGoals);

        match.RemoveGoal(Guid.NewGuid());

        Assert.Single(match.Goals);
        Assert.Equal(1, match.TeamAGoals);
        Assert.Equal(0, match.TeamBGoals);
    }

    [Fact]
    public void RemoveGoal_WhenFinalized_ShouldThrow()
    {
        var (match, p1, _, mp1, mp2) = CreateMatchWithTwoPlayers();
        match.AssignTeams(new[] { mp1.PlayerId }, new[] { mp2.PlayerId });
        match.Start();
        match.End();
        match.SetScore(0, 0);
        match.FinalizeByVotes();

        var ex = Assert.Throws<InvalidOperationException>(() => match.RemoveGoal(Guid.NewGuid()));
        Assert.Equal("Partida finalizada. Nao e possivel remover gols.", ex.Message);
    }

    [Fact]
    public void Score_ShouldBeCalculatedFromGoals_Example9x8()
    {
        var (match, a1, a2, b1, b2) = CreateMatchWithFourPlayers_TwoInA_TwoInB();

        for (int i = 0; i < 9; i++)
        {
            var scorer = (i % 2 == 0) ? a1 : a2;
            match.AddGoal(scorer.Id, assistPlayerId: null, timeSeconds: i);
        }

        for (int i = 0; i < 8; i++)
        {
            var scorer = (i % 2 == 0) ? b1 : b2;
            match.AddGoal(scorer.Id, assistPlayerId: null, timeSeconds: 100 + i);
        }

        Assert.Equal(17, match.Goals.Count);
        Assert.Equal(9, match.TeamAGoals);
        Assert.Equal(8, match.TeamBGoals);
    }
}
