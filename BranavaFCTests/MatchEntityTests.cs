using System;
using System.Linq;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Xunit;

namespace BranavaFC.Tests;

public sealed class MatchEntityTests
{
    private static (MatchEntity match, PlayerEntity p1, PlayerEntity p2, MatchPlayerEntity mp1, MatchPlayerEntity mp2)
        CreateMatchWithTwoPlayers_Created()
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Boca Jrs");

        var p1 = new PlayerEntity("A", Guid.NewGuid(), groupId, 0, false, false, Status.Active);
        var p2 = new PlayerEntity("B", Guid.NewGuid(), groupId, 0, false, false, Status.Active);

        var mp1 = new MatchPlayerEntity(p1.Id);
        var mp2 = new MatchPlayerEntity(p2.Id);

        match.AddPlayer(mp1, p1);
        match.AddPlayer(mp2, p2);

        return (match, p1, p2, mp1, mp2);
    }

    private static (MatchEntity match, PlayerEntity p1, PlayerEntity p2, MatchPlayerEntity mp1, MatchPlayerEntity mp2)
        CreateMatchWithTwoPlayers_Acceptation(bool acceptBoth = true)
    {
        var (match, p1, p2, mp1, mp2) = CreateMatchWithTwoPlayers_Created();
        match.OpenAcceptation();

        if (acceptBoth)
        {
            match.AcceptInvite(p1.Id);
            match.AcceptInvite(p2.Id);
        }

        return (match, p1, p2, mp1, mp2);
    }

    private static (MatchEntity match, PlayerEntity p1, PlayerEntity p2, MatchPlayerEntity mp1, MatchPlayerEntity mp2)
        CreateMatchWithTwoPlayers_MatchMaking(bool assignTeams = true)
    {
        var (match, p1, p2, mp1, mp2) = CreateMatchWithTwoPlayers_Acceptation(acceptBoth: true);
        match.GoToMatchMaking();

        if (assignTeams)
            match.AssignTeams(new[] { p1.Id }, new[] { p2.Id });

        return (match, p1, p2, mp1, mp2);
    }

    private static (MatchEntity match, PlayerEntity p1, PlayerEntity p2, MatchPlayerEntity mp1, MatchPlayerEntity mp2)
        CreateMatchWithTwoPlayers_Started()
    {
        var (match, p1, p2, mp1, mp2) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: true);
        match.Start();
        return (match, p1, p2, mp1, mp2);
    }

    private static (MatchEntity match, PlayerEntity p1, PlayerEntity p2, MatchPlayerEntity mp1, MatchPlayerEntity mp2)
        CreateMatchWithTwoPlayers_PostGame()
    {
        var (match, p1, p2, mp1, mp2) = CreateMatchWithTwoPlayers_Started();
        match.End();
        match.GoToPostGame();
        return (match, p1, p2, mp1, mp2);
    }

    [Fact]
    public void Ctor_WithEmptyGroupId_ShouldThrow()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new MatchEntity(Guid.Empty, DateTime.UtcNow, "X"));

        Assert.Equal("GroupId e obrigatorio.", ex.Message);
    }

    // =========================
    // STATUS FLOW
    // =========================

    [Fact]
    public void OpenAcceptation_WhenNotCreated_ShouldThrow()
    {
        var (match, p1, p2, _, _) = CreateMatchWithTwoPlayers_Created();

        match.OpenAcceptation();
        match.AcceptInvite(p1.Id);
        match.AcceptInvite(p2.Id);
        match.GoToMatchMaking();

        var ex = Assert.Throws<InvalidOperationException>(() => match.OpenAcceptation());
        Assert.Equal("So e possivel abrir acceptation quando a partida esta Created.", ex.Message);
    }

    [Fact]
    public void AcceptInvite_WhenNotAcceptation_ShouldThrow()
    {
        var (match, p1, _, _, _) = CreateMatchWithTwoPlayers_Created();

        var ex = Assert.Throws<InvalidOperationException>(() => match.AcceptInvite(p1.Id));
        Assert.Equal("So e possivel aceitar convite quando a partida esta em Acceptation.", ex.Message);
    }

    [Fact]
    public void RejectInvite_WhenNotAcceptation_ShouldThrow()
    {
        var (match, p1, _, _, _) = CreateMatchWithTwoPlayers_Created();

        var ex = Assert.Throws<InvalidOperationException>(() => match.RejectInvite(p1.Id));
        Assert.Equal("So e possivel recusar convite quando a partida esta em Acceptation.", ex.Message);
    }

    [Fact]
    public void GoToMatchMaking_WhenLessThan2Accepted_ShouldThrow()
    {
        var (match, p1, p2, _, _) = CreateMatchWithTwoPlayers_Created();
        match.OpenAcceptation();

        match.AcceptInvite(p1.Id);
        match.RejectInvite(p2.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => match.GoToMatchMaking());
        Assert.Equal("Precisa de ao menos 2 jogadores aceitos para gerar times.", ex.Message);
    }

    [Fact]
    public void Start_WhenNotMatchMaking_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();

        var ex = Assert.Throws<InvalidOperationException>(() => match.Start());
        Assert.Equal("A partida so pode ser iniciada se estiver em MatchMaking.", ex.Message);
    }

    [Fact]
    public void End_WhenNotStarted_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: true);

        var ex = Assert.Throws<InvalidOperationException>(() => match.End());
        Assert.Equal("A partida so pode ser encerrada se estiver em Started.", ex.Message);
    }

    [Fact]
    public void GoToPostGame_WhenNotEnded_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Started();

        var ex = Assert.Throws<InvalidOperationException>(() => match.GoToPostGame());
        Assert.Equal("So e possivel ir para PostGame quando a partida esta Ended.", ex.Message);
    }

    // =========================
    // UPDATE / DELETE
    // =========================

    [Fact]
    public void UpdateDetails_WhenFinalized_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_PostGame();
        match.SetScore(1, 0);
        match.FinalizeByVotes();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.UpdateDetails(match.GroupId, DateTime.UtcNow, "Novo", match.Id, match.Id));

        Assert.Equal("Partida ja Finalizada. Nao e possivel atualizar seus dados.", ex.Message);
    }

    // =========================
    // ADD PLAYER (sync) only Created or Acceptation
    // =========================

    [Fact]
    public void AddPlayer_WhenMatchMaking_ShouldThrow()
    {
        var (match, p1, p2, mp1, mp2) = CreateMatchWithTwoPlayers_Created();
        match.OpenAcceptation();
        match.AcceptInvite(p1.Id);
        match.AcceptInvite(p2.Id);
        match.GoToMatchMaking();

        var p3 = new PlayerEntity("C", Guid.NewGuid(), match.GroupId, 0, false, false, Status.Active);
        var mp3 = new MatchPlayerEntity(p3.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => match.AddPlayer(mp3, p3));
        Assert.Equal("So e possivel sincronizar jogadores quando a partida esta Created ou Acceptation.", ex.Message);
    }

    [Fact]
    public void AddPlayer_WhenAcceptation_ShouldSucceed()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();
        match.OpenAcceptation();

        var p3 = new PlayerEntity("C", Guid.NewGuid(), match.GroupId, 0, false, false, Status.Active);
        var mp3 = new MatchPlayerEntity(p3.Id);

        // Should NOT throw — Acceptation is now a valid state for AddPlayer (used by rewind resync)
        match.AddPlayer(mp3, p3);
        Assert.Contains(match.Players, mp => mp.PlayerId == p3.Id);
    }

    [Fact]
    public void AddPlayer_ShouldIgnoreDuplicatesByPlayerId()
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Local");
        var p1 = new PlayerEntity("A", Guid.NewGuid(), groupId, 0, false, false, Status.Active);

        var mp1 = new MatchPlayerEntity(p1.Id);
        match.AddPlayer(mp1, p1);

        var mp2 = new MatchPlayerEntity(p1.Id);
        match.AddPlayer(mp2, p1);

        Assert.Single(match.Players);
    }

    // =========================
    // ASSIGN / SWAP in MatchMaking
    // =========================

    [Fact]
    public void AssignTeams_WhenNotMatchMaking_ShouldThrow()
    {
        var (match, p1, p2, _, _) = CreateMatchWithTwoPlayers_Acceptation(acceptBoth: true);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.AssignTeams(new[] { p1.Id }, new[] { p2.Id }));

        Assert.Equal("So e possivel atribuir times quando a partida esta em MatchMaking.", ex.Message);
    }

    [Fact]
    public void AssignTeams_WithNotAcceptedInvite_ShouldThrow()
    {
        var (match, p1, p2, p3) = CreateMatchWithThreePlayers_Created();

        match.OpenAcceptation();

        match.AcceptInvite(p1.Id);
        match.AcceptInvite(p2.Id);

        match.GoToMatchMaking();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.AssignTeams(
                teamAPlayerIds: new[] { p1.Id },
                teamBPlayerIds: new[] { p3.Id }));

        Assert.Equal("Ha jogadores que ainda nao aceitaram o convite.", ex.Message);
    }

    [Fact]
    public void GoToMatchMaking_WithLessThan2Accepted_ShouldThrow()
    {
        var (match, p1, p2, _, _) = CreateMatchWithTwoPlayers_Created();

        match.OpenAcceptation();
        match.AcceptInvite(p1.Id);
        match.RejectInvite(p2.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => match.GoToMatchMaking());

        Assert.Equal("Precisa de ao menos 2 jogadores aceitos para gerar times.", ex.Message);
    }

    [Fact]
    public void SwapPlayers_WhenNotMatchMaking_ShouldThrow()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers_Started();

        var ex = Assert.Throws<InvalidOperationException>(() => match.SwapPlayers(mp1.Id, mp2.Id));
        Assert.Equal("So e possivel trocar jogadores quando a partida esta em MatchMaking.", ex.Message);
    }

    [Fact]
    public void SwapPlayers_ShouldSwapTeams()
    {
        var (match, p1, p2, mp1, mp2) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: true);

        Assert.Equal((short)1, match.Players.First(x => x.PlayerId == p1.Id).Team);
        Assert.Equal((short)2, match.Players.First(x => x.PlayerId == p2.Id).Team);

        match.SwapPlayers(mp1.Id, mp2.Id);

        Assert.Equal((short)2, match.Players.First(x => x.PlayerId == p1.Id).Team);
        Assert.Equal((short)1, match.Players.First(x => x.PlayerId == p2.Id).Team);
    }

    // =========================
    // TEAM COLORS only MatchMaking
    // =========================

    [Fact]
    public void SetTeamColors_WhenNotMatchMaking_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Acceptation(acceptBoth: true);

        var ex = Assert.Throws<InvalidOperationException>(() => match.SetTeamColors(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal("So e possivel setar cores quando a partida esta em MatchMaking.", ex.Message);
    }

    [Fact]
    public void SetTeamColors_WithSameColor_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: false);

        var colorId = Guid.NewGuid();

        var ex = Assert.Throws<InvalidOperationException>(() => match.SetTeamColors(colorId, colorId));
        Assert.Equal("Os dois times nao podem possuir a mesma cor.", ex.Message);
    }

    // =========================
    // SCORE / VOTE / FINALIZE only PostGame
    // =========================

    [Fact]
    public void SetScore_WhenNotPostGame_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Started();

        var ex = Assert.Throws<InvalidOperationException>(() => match.SetScore(1, 0));
        Assert.Equal("So e possivel setar placar quando a partida esta em PostGame.", ex.Message);
    }

    [Fact]
    public void CreateVote_WhenNotPostGame_ShouldThrow()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers_Started();

        var ex = Assert.Throws<InvalidOperationException>(() => match.CreateVote(mp1.Id, mp2.Id));
        Assert.Equal("So e possivel votar no MVP quando a partida esta em PostGame.", ex.Message);
    }

    [Fact]
    public void Finalize_WhenNotPostGame_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Started();

        var ex = Assert.Throws<InvalidOperationException>(() => match.FinalizeByVotes());
        Assert.Equal("A partida so pode ser finalizada se estiver em PostGame.", ex.Message);
    }

    [Fact]
    public void Finalize_WithoutScoreAndWithoutGoals_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_PostGame();

        var ex = Assert.Throws<InvalidOperationException>(() => match.FinalizeByVotes());
        Assert.Equal("Para finalizar a partida, o placar deve estar definido (placar ou gols).", ex.Message);
    }

    [Fact]
    public void Finalize_WithGoalsButNoScore_ShouldRecalculateAndFinalize()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();

        match.AddGoalByMatchPlayer(mp1.Id, null, 10);

        match.FinalizeByVotes();

        Assert.Equal(MatchStatus.Finalized, match.Status);
        Assert.Equal(1, match.TeamAGoals);
        Assert.Equal(0, match.TeamBGoals);
    }

    [Fact]
    public void CreateVote_WhenPostGame_ShouldPreventDoubleVote_AndSetReceivedVote()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers_PostGame();

        match.SetScore(1, 0);

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
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers_PostGame();
        match.SetScore(1, 0);

        var v1 = match.CreateVote(mp1.Id, mp2.Id);
        match.Votes.Add(v1);

        var mvp = match.GetComputedMvp();

        Assert.NotNull(mvp);
        Assert.Equal(mp2.Id, mvp!.Id);
    }

    // =========================
    // GOALS: Started or PostGame
    // =========================

    [Fact]
    public void AddGoal_WhenNotStartedOrPostGame_ShouldThrow()
    {
        var (match, p1, _, _, _) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: true);

        var scorerMp = match.Players.First(x => x.PlayerId == p1.Id);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            match.AddGoalByMatchPlayer(scorerMp.Id, null, 10));

        Assert.Equal("So e possivel registrar gols quando a partida esta Started, PostGame ou Finalized.", ex.Message);
    }

    [Fact]
    public void AddGoal_WhenStatusStarted_ShouldWork_AndRecalculateScore()
    {
        var (match, p1, _, _, _) = CreateMatchWithTwoPlayers_Started();

        var scorerMp = match.Players.First(p => p.PlayerId == p1.Id);

        match.AddGoalByMatchPlayer(scorerMp.Id, null, 10);

        Assert.Single(match.Goals);
        Assert.Equal(1, match.TeamAGoals);
        Assert.Equal(0, match.TeamBGoals);
    }

    [Fact]
    public void AddGoal_WhenStatusPostGame_ShouldWork_AndRecalculateScore()
    {
        var (match, p1, _, _, _) = CreateMatchWithTwoPlayers_PostGame();

        var scorerMp = match.Players.First(p => p.PlayerId == p1.Id);

        match.AddGoalByMatchPlayer(scorerMp.Id, null, 10);

        Assert.Single(match.Goals);
        Assert.Equal(1, match.TeamAGoals);
        Assert.Equal(0, match.TeamBGoals);
    }

    [Fact]
    public void RemoveGoal_WhenFinalized_ShouldWork()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();

        match.AddGoalByMatchPlayer(mp1.Id, null, 10);
        match.FinalizeByVotes();

        var goalId = match.Goals.First().Id;
        match.RemoveGoal(goalId);

        Assert.Empty(match.Goals);
    }

    private static (MatchEntity match, PlayerEntity p1, PlayerEntity p2, PlayerEntity p3)
    CreateMatchWithThreePlayers_Created()
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Local");

        var p1 = new PlayerEntity("P1", Guid.NewGuid(), groupId, 0, false, false, Status.Active);
        var p2 = new PlayerEntity("P2", Guid.NewGuid(), groupId, 0, false, false, Status.Active);
        var p3 = new PlayerEntity("P3", Guid.NewGuid(), groupId, 0, false, false, Status.Active);

        match.AddPlayer(new MatchPlayerEntity(p1.Id), p1);
        match.AddPlayer(new MatchPlayerEntity(p2.Id), p2);
        match.AddPlayer(new MatchPlayerEntity(p3.Id), p3);

        return (match, p1, p2, p3);
    }

    [Fact]
    public void Rewind_FromMatchMaking_ShouldGoToAcceptation()
    {
        var (match, p1, p2, _, _) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: true);

        match.SetTeamColors(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(MatchStatus.MatchMaking, match.Status);

        match.RewindOneStep();

        Assert.Equal(MatchStatus.Acceptation, match.Status);
    }

    [Fact]
    public void Rewind_FromStarted_ShouldGoToMatchMaking()
    {
        var (match, p1, _, _, _) = CreateMatchWithTwoPlayers_Started();

        var scorerMp = match.Players.First(x => x.PlayerId == p1.Id);
        match.AddGoalByMatchPlayer(scorerMp.Id, null, 10);

        Assert.Equal(MatchStatus.Started, match.Status);
        Assert.NotEmpty(match.Goals);

        match.RewindOneStep();

        Assert.Equal(MatchStatus.MatchMaking, match.Status);
    }

    [Fact]
    public void Rewind_FromEnded_ShouldGoToStarted()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Started();
        match.End();

        Assert.Equal(MatchStatus.Ended, match.Status);

        match.RewindOneStep();

        Assert.Equal(MatchStatus.Started, match.Status);
    }

    [Fact]
    public void Rewind_FromPostGame_ShouldGoToEnded()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers_PostGame();

        match.SetScore(1, 0);
        var vote = match.CreateVote(mp1.Id, mp2.Id);
        match.Votes.Add(vote);

        Assert.Equal(MatchStatus.PostGame, match.Status);

        match.RewindOneStep();

        Assert.Equal(MatchStatus.Ended, match.Status);
    }

    [Fact]
    public void Rewind_FromAcceptation_ShouldGoToCreated()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Acceptation(acceptBoth: true);

        Assert.Equal(MatchStatus.Acceptation, match.Status);

        match.RewindOneStep();

        Assert.Equal(MatchStatus.Created, match.Status);
    }

    [Fact]
    public void Rewind_FromCreated_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();

        var ex = Assert.Throws<InvalidOperationException>(() => match.RewindOneStep());
        Assert.Equal("Nao e possivel voltar status quando a partida esta Created.", ex.Message);
    }

    [Fact]
    public void Rewind_FromFinalized_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_PostGame();
        match.SetScore(1, 0);
        match.FinalizeByVotes();

        var ex = Assert.Throws<InvalidOperationException>(() => match.RewindOneStep());
        Assert.Equal("Partida finalizada. Nao e possivel voltar status.", ex.Message);
    }

    // =========================
    // AUTO SET MVP IF ALL VOTED
    // =========================

    private static (MatchEntity match, PlayerEntity p1, PlayerEntity p2, PlayerEntity p3,
        MatchPlayerEntity mp1, MatchPlayerEntity mp2, MatchPlayerEntity mp3)
        CreateMatchWithThreePlayers_PostGame()
    {
        var (match, p1, p2, p3) = CreateMatchWithThreePlayers_Created();
        match.OpenAcceptation();
        match.AcceptInvite(p1.Id);
        match.AcceptInvite(p2.Id);
        match.AcceptInvite(p3.Id);
        match.GoToMatchMaking();
        // p1 → time 1; p2 + p3 → time 2
        match.AssignTeams(new[] { p1.Id }, new[] { p2.Id, p3.Id });
        match.Start();
        match.End();
        match.GoToPostGame();

        var mp1 = match.Players.First(x => x.PlayerId == p1.Id);
        var mp2 = match.Players.First(x => x.PlayerId == p2.Id);
        var mp3 = match.Players.First(x => x.PlayerId == p3.Id);

        return (match, p1, p2, p3, mp1, mp2, mp3);
    }

    [Fact]
    public void AutoSetMvpIfAllVoted_WhenNoVotes_ShouldReturnFalse()
    {
        var (match, _, _, _, _, _, _) = CreateMatchWithThreePlayers_PostGame();

        var result = match.AutoSetMvpIfAllVoted();

        Assert.False(result);
        Assert.DoesNotContain(match.Players, p => p.IsMvp == true);
    }

    [Fact]
    public void AutoSetMvpIfAllVoted_WhenOnlyOneOfThreeVoted_ShouldReturnFalse()
    {
        var (match, _, _, _, mp1, mp2, _) = CreateMatchWithThreePlayers_PostGame();

        var v = match.CreateVote(mp1.Id, mp2.Id);
        match.Votes.Add(v);

        var result = match.AutoSetMvpIfAllVoted();

        Assert.False(result);
        Assert.DoesNotContain(match.Players, p => p.IsMvp == true);
    }

    [Fact]
    public void AutoSetMvpIfAllVoted_WhenAllParticipantsVoted_ShouldReturnTrue_AndSetMvpOnWinner()
    {
        // votos: mp1→mp2 (1), mp2→mp3 (1), mp3→mp2 (2) → mp2 vence
        var (match, _, _, _, mp1, mp2, mp3) = CreateMatchWithThreePlayers_PostGame();

        match.Votes.Add(match.CreateVote(mp1.Id, mp2.Id));
        match.Votes.Add(match.CreateVote(mp2.Id, mp3.Id));
        match.Votes.Add(match.CreateVote(mp3.Id, mp2.Id));

        var result = match.AutoSetMvpIfAllVoted();

        Assert.True(result);
        Assert.True(mp2.IsMvp);
        Assert.NotEqual(true, mp1.IsMvp);
        Assert.NotEqual(true, mp3.IsMvp);
    }

    [Fact]
    public void AutoSetMvpIfAllVoted_ShouldNotRequireGuestParticipantToVote()
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Local");

        var p1    = new PlayerEntity("P1",    Guid.NewGuid(), groupId, 0, false, false, Status.Active);
        var p2    = new PlayerEntity("P2",    Guid.NewGuid(), groupId, 0, false, false, Status.Active);
        var guest = new PlayerEntity("Guest", null,           groupId, 0, false, true,  Status.Active);

        var mp1 = new MatchPlayerEntity(p1.Id);
        var mp2 = new MatchPlayerEntity(p2.Id);
        var mpG = new MatchPlayerEntity(guest.Id);

        match.AddPlayer(mp1, p1);
        match.AddPlayer(mp2, p2);
        match.AddPlayer(mpG, guest);

        match.OpenAcceptation();
        match.AcceptInvite(p1.Id);
        match.AcceptInvite(p2.Id);
        match.AcceptInvite(guest.Id);
        match.GoToMatchMaking();
        // convidado não é atribuído a nenhum time (team = 0)
        match.AssignTeams(new[] { p1.Id }, new[] { p2.Id });
        match.Start();
        match.End();
        match.GoToPostGame();

        match.Votes.Add(match.CreateVote(mp1.Id, mp2.Id));
        match.Votes.Add(match.CreateVote(mp2.Id, mp1.Id));

        // retorna true mesmo sem o convidado votar
        var result = match.AutoSetMvpIfAllVoted();

        Assert.True(result);
    }

    [Fact]
    public void AutoSetMvpIfAllVoted_ShouldNotRequireUnassignedNonGuestToVote()
    {
        // p3 aceitou mas ficou sem time (team = 0) → não entra em eligible
        var (match, p1, p2, p3) = CreateMatchWithThreePlayers_Created();

        match.OpenAcceptation();
        match.AcceptInvite(p1.Id);
        match.AcceptInvite(p2.Id);
        match.AcceptInvite(p3.Id);
        match.GoToMatchMaking();
        match.AssignTeams(new[] { p1.Id }, new[] { p2.Id }); // p3 fica team = 0
        match.Start();
        match.End();
        match.GoToPostGame();

        var mp1 = match.Players.First(x => x.PlayerId == p1.Id);
        var mp2 = match.Players.First(x => x.PlayerId == p2.Id);

        match.Votes.Add(match.CreateVote(mp1.Id, mp2.Id));
        match.Votes.Add(match.CreateVote(mp2.Id, mp1.Id));

        var result = match.AutoSetMvpIfAllVoted();

        Assert.True(result);
    }

    [Fact]
    public void AutoSetMvpIfAllVoted_WhenAllParticipantsAreGuests_ShouldReturnFalse()
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Local");

        var g1 = new PlayerEntity("G1", null, groupId, 0, false, true, Status.Active);
        var g2 = new PlayerEntity("G2", null, groupId, 0, false, true, Status.Active);

        match.AddPlayer(new MatchPlayerEntity(g1.Id), g1);
        match.AddPlayer(new MatchPlayerEntity(g2.Id), g2);

        match.OpenAcceptation();
        match.AcceptInvite(g1.Id);
        match.AcceptInvite(g2.Id);
        match.GoToMatchMaking();
        match.AssignTeams(new[] { g1.Id }, new[] { g2.Id });
        match.Start();
        match.End();
        match.GoToPostGame();

        // eligible = [] → retorna false imediatamente
        var result = match.AutoSetMvpIfAllVoted();

        Assert.False(result);
    }
}