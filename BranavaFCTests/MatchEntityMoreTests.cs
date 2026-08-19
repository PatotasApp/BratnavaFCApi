using System;
using System.Linq;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>
/// Testes adicionais de MatchEntity cobrindo branches não exercitados por MatchEntityTests.
/// </summary>
public sealed class MatchEntityMoreTests
{
    private static (MatchEntity match, PlayerEntity p1, PlayerEntity p2, MatchPlayerEntity mp1, MatchPlayerEntity mp2)
        CreateMatchWithTwoPlayers_Created(Guid? groupId = null)
    {
        var gid = groupId ?? Guid.NewGuid();
        var match = new MatchEntity(gid, DateTime.UtcNow, "Boca Jrs");

        var p1 = new PlayerEntity("A", Guid.NewGuid(), gid, 0, false, false, Status.Active);
        var p2 = new PlayerEntity("B", Guid.NewGuid(), gid, 0, false, false, Status.Active);

        var mp1 = new MatchPlayerEntity(p1.Id);
        var mp2 = new MatchPlayerEntity(p2.Id);

        match.AddPlayer(mp1, p1);
        match.AddPlayer(mp2, p2);

        return (match, p1, p2, mp1, mp2);
    }

    private static (MatchEntity match, PlayerEntity p1, PlayerEntity p2, MatchPlayerEntity mp1, MatchPlayerEntity mp2)
        CreateMatchWithTwoPlayers_MatchMaking(bool assignTeams = true)
    {
        var (match, p1, p2, mp1, mp2) = CreateMatchWithTwoPlayers_Created();
        match.OpenAcceptation();
        match.AcceptInvite(p1.Id);
        match.AcceptInvite(p2.Id);
        match.GoToMatchMaking();

        if (assignTeams)
            match.AssignTeams(new[] { p1.Id }, new[] { p2.Id });

        return (match, p1, p2, mp1, mp2);
    }

    private static (MatchEntity match, PlayerEntity p1, PlayerEntity p2, MatchPlayerEntity mp1, MatchPlayerEntity mp2)
        CreateMatchWithTwoPlayers_PostGame()
    {
        var (match, p1, p2, mp1, mp2) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: true);
        match.Start();
        match.End();
        match.GoToPostGame();
        return (match, p1, p2, mp1, mp2);
    }

    /// <summary>Partida em PostGame com 4 jogadores (2 por time).</summary>
    private static (MatchEntity match, MatchPlayerEntity[] mps) CreatePostGameWithFourPlayers()
    {
        var gid = Guid.NewGuid();
        var match = new MatchEntity(gid, DateTime.UtcNow, "Boca Jrs");

        var players = Enumerable.Range(1, 4)
            .Select(i => new PlayerEntity($"P{i}", Guid.NewGuid(), gid, 0, false, false, Status.Active))
            .ToArray();

        var mps = players.Select(p => new MatchPlayerEntity(p.Id)).ToArray();
        for (var i = 0; i < 4; i++)
            match.AddPlayer(mps[i], players[i]);

        match.OpenAcceptation();
        foreach (var p in players)
            match.AcceptInvite(p.Id);

        match.GoToMatchMaking();
        match.AssignTeams(
            new[] { players[0].Id, players[1].Id },
            new[] { players[2].Id, players[3].Id });
        match.Start();
        match.End();
        match.GoToPostGame();

        return (match, mps);
    }

    // =========================
    // CTOR / UPDATE DETAILS
    // =========================

    [Fact]
    public void Ctor_WithNullPlaceName_ShouldThrowArgumentNull()
    {
        var act = () => new MatchEntity(Guid.NewGuid(), DateTime.UtcNow, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UpdateDetails_WithEmptyGroupId_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();

        var act = () => match.UpdateDetails(Guid.Empty, DateTime.UtcNow, "X", match.Id, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("GroupId e obrigatorio.");
    }

    [Fact]
    public void UpdateDetails_WithWrongGroupId_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();

        var act = () => match.UpdateDetails(Guid.NewGuid(), DateTime.UtcNow, "X", match.Id, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("GroupId informado nao pertence a esta partida.");
    }

    [Fact]
    public void UpdateDetails_WithDtoIdMismatch_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();

        var act = () => match.UpdateDetails(match.GroupId, DateTime.UtcNow, "X", match.Id, Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Id do payload nao bate com o Id da rota.");
    }

    [Fact]
    public void UpdateDetails_WithBlankPlaceName_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();

        var act = () => match.UpdateDetails(match.GroupId, DateTime.UtcNow, "  ", match.Id, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Local da partida e obrigatorio.");
    }

    [Fact]
    public void UpdateDetails_WhenValid_ShouldUpdateFields()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();
        var newDate = DateTime.UtcNow.AddDays(3);

        match.UpdateDetails(match.GroupId, newDate, "River Plate", match.Id, match.Id);

        match.PlayedAt.Should().Be(newDate);
        match.PlaceName.Should().Be("River Plate");
    }

    [Fact]
    public void EnsureCanDelete_WhenNotFinalized_ShouldNotThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();

        var act = () => match.EnsureCanDelete();

        act.Should().NotThrow();
    }

    [Fact]
    public void SetLinkedPoll_ShouldSetIdAndUpdateDate()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();
        var pollId = Guid.NewGuid();

        match.SetLinkedPoll(pollId);
        match.LinkedPollId.Should().Be(pollId);

        match.SetLinkedPoll(null);
        match.LinkedPollId.Should().BeNull();
    }

    // =========================
    // INVITES
    // =========================

    [Fact]
    public void AcceptInvite_WithEmptyPlayerId_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();
        match.OpenAcceptation();

        var act = () => match.AcceptInvite(Guid.Empty);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("PlayerId e obrigatorio.");
    }

    [Fact]
    public void AcceptInvite_WithUnknownPlayer_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();
        match.OpenAcceptation();

        var act = () => match.AcceptInvite(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Jogador nao encontrado nesta partida.");
    }

    [Fact]
    public void RejectInvite_WithEmptyPlayerId_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();
        match.OpenAcceptation();

        var act = () => match.RejectInvite(Guid.Empty);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("PlayerId e obrigatorio.");
    }

    // =========================
    // START / SCORE
    // =========================

    [Fact]
    public void Start_WithoutTeamsAssigned_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: false);

        var act = () => match.Start();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Nao e possivel iniciar a partida sem os times estarem definidos.");
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void SetScore_WithNegativeValues_ShouldThrow(int a, int b)
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_PostGame();

        var act = () => match.SetScore(a, b);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Placar nao pode ser negativo.");
    }

    // =========================
    // TEAM COLORS
    // =========================

    [Fact]
    public void SetTeamColorsRandomly_WhenNotMatchMaking_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();
        var colors = new[] { new TeamColorEntity(match.GroupId, "Azul", "#0000FF") };

        var act = () => match.SetTeamColorsRandomly(colors);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("So e possivel setar cores quando a partida esta em MatchMaking.");
    }

    [Fact]
    public void SetTeamColorsRandomly_WithNoColors_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_MatchMaking();

        var act = () => match.SetTeamColorsRandomly(Array.Empty<TeamColorEntity>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Nao ha cores cadastradas para sortear.");
    }

    [Fact]
    public void SetTeamColorsRandomly_WithSingleColor_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_MatchMaking();
        var colors = new[] { new TeamColorEntity(match.GroupId, "Azul", "#0000FF") };

        var act = () => match.SetTeamColorsRandomly(colors);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Nao foi possivel sortear duas cores distintas.");
    }

    [Fact]
    public void SetTeamColorsRandomly_WithTwoColors_ShouldAssignDistinctColors()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_MatchMaking();
        var c1 = new TeamColorEntity(match.GroupId, "Azul", "#0000FF");
        var c2 = new TeamColorEntity(match.GroupId, "Verde", "#00FF00");

        match.SetTeamColorsRandomly(new[] { c1, c2 });

        match.TeamAColorId.Should().NotBeNull();
        match.TeamBColorId.Should().NotBeNull();
        match.TeamAColorId.Should().NotBe(match.TeamBColorId!.Value);
        new[] { c1.Id, c2.Id }.Should().Contain(match.TeamAColorId!.Value);
        new[] { c1.Id, c2.Id }.Should().Contain(match.TeamBColorId!.Value);
    }

    // =========================
    // VOTES
    // =========================

    [Fact]
    public void CreateVote_WithEmptyIds_ShouldThrow()
    {
        var (match, _, _, _, mp2) = CreateMatchWithTwoPlayers_PostGame();

        var act = () => match.CreateVote(Guid.Empty, mp2.Id);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("O jogador que votou e o votado sao obrigatorios.");
    }

    [Fact]
    public void CreateVote_VotingForSelf_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();

        var act = () => match.CreateVote(mp1.Id, mp1.Id);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("O jogador nao pode votar em si mesmo.");
    }

    [Fact]
    public void CreateVote_WhenVoterNotInMatch_ShouldThrow()
    {
        var (match, _, _, _, mp2) = CreateMatchWithTwoPlayers_PostGame();

        var act = () => match.CreateVote(Guid.NewGuid(), mp2.Id);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Apenas jogadores da partida podem votar.");
    }

    [Fact]
    public void CreateVote_WhenVotedForNotInMatch_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();

        var act = () => match.CreateVote(mp1.Id, Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Apenas jogadores que jogaram podem ser votados.");
    }

    [Fact]
    public void CreateVote_WhenVoterIsGuest_ShouldThrow()
    {
        var gid = Guid.NewGuid();
        var match = new MatchEntity(gid, DateTime.UtcNow, "Boca Jrs");

        var p1 = new PlayerEntity("Convidado", null, gid, 0, false, isGuest: true, Status.Active);
        var p2 = new PlayerEntity("Normal", Guid.NewGuid(), gid, 0, false, false, Status.Active);

        var mp1 = new MatchPlayerEntity(p1.Id);
        var mp2 = new MatchPlayerEntity(p2.Id);
        match.AddPlayer(mp1, p1);
        match.AddPlayer(mp2, p2);

        match.OpenAcceptation();
        match.AcceptInvite(p1.Id);
        match.AcceptInvite(p2.Id);
        match.GoToMatchMaking();
        match.AssignTeams(new[] { p1.Id }, new[] { p2.Id });
        match.Start();
        match.End();
        match.GoToPostGame();

        var act = () => match.CreateVote(mp1.Id, mp2.Id);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Convidados não podem votar no MVP.");
    }

    // =========================
    // MVP TIE RULES
    // =========================

    [Fact]
    public void FinalizeByVotes_WithTieAndNoMvpRule_ShouldSetNoMvp()
    {
        var (match, mps) = CreatePostGameWithFourPlayers();
        match.SetScore(1, 0);

        // Empate: mp0 e mp1 recebem 2 votos cada (todos os 4 votam)
        match.CreateVote(mps[0].Id, mps[1].Id);
        match.CreateVote(mps[1].Id, mps[0].Id);
        match.CreateVote(mps[2].Id, mps[0].Id);
        match.CreateVote(mps[3].Id, mps[1].Id);

        match.FinalizeByVotes(MvpTieRule.NoMvp, 2);

        match.Status.Should().Be(MatchStatus.Finalized);
        match.Players.Should().OnlyContain(p => p.IsMvp != true);
    }

    [Fact]
    public void FinalizeByVotes_WithTieAndAllMvpUpToMax_WithinLimit_ShouldSetAllTied()
    {
        var (match, mps) = CreatePostGameWithFourPlayers();
        match.SetScore(1, 0);

        match.CreateVote(mps[0].Id, mps[1].Id);
        match.CreateVote(mps[1].Id, mps[0].Id);
        match.CreateVote(mps[2].Id, mps[0].Id);
        match.CreateVote(mps[3].Id, mps[1].Id);

        match.FinalizeByVotes(MvpTieRule.AllMvpUpToMax, 2);

        match.Players.Count(p => p.IsMvp == true).Should().Be(2);
    }

    [Fact]
    public void FinalizeByVotes_WithSingleLeaderAndUpToMaxRule_ShouldSetSingleMvp()
    {
        var (match, mps) = CreatePostGameWithFourPlayers();
        match.SetScore(1, 0);

        // mp1 = 2 votos, mp2 = 1, mp3 = 1 → líder único recebe MVP independente da regra
        match.CreateVote(mps[0].Id, mps[1].Id);
        match.CreateVote(mps[1].Id, mps[2].Id);
        match.CreateVote(mps[2].Id, mps[3].Id);
        match.CreateVote(mps[3].Id, mps[1].Id);

        match.FinalizeByVotes(MvpTieRule.AllMvpUpToMax, 2);

        match.Players.Single(p => p.IsMvp == true).Id.Should().Be(mps[1].Id);
    }

    [Fact]
    public void FinalizeByVotes_WithFourWayTieAboveMax_ShouldSetNoMvp()
    {
        var (match, mps) = CreatePostGameWithFourPlayers();
        match.SetScore(1, 0);

        // Ciclo de votos: cada jogador recebe exatamente 1 voto (empate de 4)
        match.CreateVote(mps[0].Id, mps[1].Id);
        match.CreateVote(mps[1].Id, mps[2].Id);
        match.CreateVote(mps[2].Id, mps[3].Id);
        match.CreateVote(mps[3].Id, mps[0].Id);

        match.FinalizeByVotes(MvpTieRule.AllMvpUpToMax, 2);

        // 4 empatados > max 2 → ninguém é MVP
        match.Players.Should().OnlyContain(p => p.IsMvp != true);
    }

    [Fact]
    public void GetComputedMvps_WithNoVotes_ShouldReturnEmpty()
    {
        var (match, _) = CreatePostGameWithFourPlayers();

        match.GetComputedMvps().Should().BeEmpty();
        match.GetComputedMvp().Should().BeNull();
    }

    // =========================
    // ADD PLAYER
    // =========================

    [Fact]
    public void AddPlayer_WithNullArgs_ShouldThrow()
    {
        var (match, p1, _, _, _) = CreateMatchWithTwoPlayers_Created();

        var actMp = () => match.AddPlayer(null!, p1);
        actMp.Should().Throw<ArgumentNullException>();

        var actPl = () => match.AddPlayer(new MatchPlayerEntity(Guid.NewGuid()), null!);
        actPl.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddPlayer_FromAnotherGroup_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_Created();
        var alien = new PlayerEntity("Alien", Guid.NewGuid(), Guid.NewGuid(), 0, false, false, Status.Active);

        var act = () => match.AddPlayer(new MatchPlayerEntity(alien.Id), alien);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Player nao pertence ao mesmo Group da partida.");
    }

    // =========================
    // ASSIGN TEAMS
    // =========================

    [Fact]
    public void AssignTeams_WithEmptyTeam_ShouldThrow()
    {
        var (match, p1, _, _, _) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: false);

        var act = () => match.AssignTeams(new[] { p1.Id }, Array.Empty<Guid>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Os dois times devem ter ao menos 1 jogador.");
    }

    [Fact]
    public void AssignTeams_WithDuplicateIdsInTeamA_ShouldThrow()
    {
        var (match, p1, p2, _, _) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: false);

        var act = () => match.AssignTeams(new[] { p1.Id, p1.Id }, new[] { p2.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Time A contem IDs repetidos.");
    }

    [Fact]
    public void AssignTeams_WithDuplicateIdsInTeamB_ShouldThrow()
    {
        var (match, p1, p2, _, _) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: false);

        var act = () => match.AssignTeams(new[] { p1.Id }, new[] { p2.Id, p2.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Time B contem IDs repetidos.");
    }

    [Fact]
    public void AssignTeams_WithPlayerInBothTeams_ShouldThrow()
    {
        var (match, p1, p2, _, _) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: false);

        var act = () => match.AssignTeams(new[] { p1.Id, p2.Id }, new[] { p2.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Ha jogadores atribuidos aos dois times ao mesmo tempo.");
    }

    [Fact]
    public void AssignTeams_WithUnknownPlayer_ShouldThrow()
    {
        var (match, p1, _, _, _) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: false);

        var act = () => match.AssignTeams(new[] { p1.Id }, new[] { Guid.NewGuid() });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Ha jogadores que nao pertencem a esta partida.");
    }

    // =========================
    // PLAYER ROLE / SWAP
    // =========================

    [Fact]
    public void SetPlayerRole_WhenWrongStatus_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_Created();

        var act = () => match.SetPlayerRole(mp1.Id, true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Função só pode ser alterada durante Acceptation ou MatchMaking.");
    }

    [Fact]
    public void SetPlayerRole_WhenPlayerNotFound_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: false);

        var act = () => match.SetPlayerRole(Guid.NewGuid(), true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Jogador não encontrado na partida.");
    }

    [Fact]
    public void SetPlayerRole_WhenValid_ShouldSetGoalkeeper()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_MatchMaking(assignTeams: false);

        match.SetPlayerRole(mp1.Id, true);

        mp1.IsGoalkeeper.Should().BeTrue();
    }

    [Fact]
    public void SwapPlayers_WithEmptyIds_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_MatchMaking();

        var act = () => match.SwapPlayers(Guid.Empty, mp1.Id);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Os dois jogadores sao obrigatorios.");
    }

    [Fact]
    public void SwapPlayers_WithSamePlayer_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_MatchMaking();

        var act = () => match.SwapPlayers(mp1.Id, mp1.Id);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Nao e possivel trocar o mesmo jogador.");
    }

    [Fact]
    public void SwapPlayers_WhenPlayerNotFound_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_MatchMaking();

        var act = () => match.SwapPlayers(mp1.Id, Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Jogador nao encontrado nesta partida.");
    }

    [Fact]
    public void SwapPlayers_WithUnassignedPlayer_ShouldThrow()
    {
        // Partida em MatchMaking com um jogador sem time atribuído
        var gid = Guid.NewGuid();
        var m = new MatchEntity(gid, DateTime.UtcNow, "X");
        var pl = Enumerable.Range(1, 3)
            .Select(i => new PlayerEntity($"J{i}", Guid.NewGuid(), gid, 0, false, false, Status.Active))
            .ToArray();
        var mp = pl.Select(p => new MatchPlayerEntity(p.Id)).ToArray();
        for (var i = 0; i < 3; i++) m.AddPlayer(mp[i], pl[i]);
        m.OpenAcceptation();
        foreach (var p in pl) m.AcceptInvite(p.Id);
        m.GoToMatchMaking();
        m.AssignTeams(new[] { pl[0].Id }, new[] { pl[1].Id }); // pl[2] fica sem time

        var act = () => m.SwapPlayers(mp[0].Id, mp[2].Id);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Nao e possivel trocar jogadores nao atribuidos a times.");
    }

    [Fact]
    public void SwapPlayers_WhenSameTeam_ShouldThrow()
    {
        var gid = Guid.NewGuid();
        var m = new MatchEntity(gid, DateTime.UtcNow, "X");
        var pl = Enumerable.Range(1, 4)
            .Select(i => new PlayerEntity($"J{i}", Guid.NewGuid(), gid, 0, false, false, Status.Active))
            .ToArray();
        var mp = pl.Select(p => new MatchPlayerEntity(p.Id)).ToArray();
        for (var i = 0; i < 4; i++) m.AddPlayer(mp[i], pl[i]);
        m.OpenAcceptation();
        foreach (var p in pl) m.AcceptInvite(p.Id);
        m.GoToMatchMaking();
        m.AssignTeams(new[] { pl[0].Id, pl[1].Id }, new[] { pl[2].Id, pl[3].Id });

        var act = () => m.SwapPlayers(mp[0].Id, mp[1].Id);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Os dois jogadores estao no mesmo time.");
    }

    // =========================
    // GOALS
    // =========================

    [Fact]
    public void AddGoalByMatchPlayer_WithEmptyScorer_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_PostGame();

        var act = () => match.AddGoalByMatchPlayer(Guid.Empty, null, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("MatchPlayerId do gol e obrigatorio.");
    }

    [Fact]
    public void AddGoalByMatchPlayer_WithNegativeTime_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();

        var act = () => match.AddGoalByMatchPlayer(mp1.Id, null, -5);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Tempo do gol nao pode ser negativo.");
    }

    [Fact]
    public void AddGoalByMatchPlayer_WithUnknownScorer_ShouldThrow()
    {
        var (match, _, _, _, _) = CreateMatchWithTwoPlayers_PostGame();

        var act = () => match.AddGoalByMatchPlayer(Guid.NewGuid(), null, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("O jogador do gol nao pertence a esta partida.");
    }

    [Fact]
    public void AddGoalByMatchPlayer_WithAssistEmptyGuid_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();

        var act = () => match.AddGoalByMatchPlayer(mp1.Id, Guid.Empty, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("AssistMatchPlayerId invalido.");
    }

    [Fact]
    public void AddGoalByMatchPlayer_WithAssistSameAsScorer_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();

        var act = () => match.AddGoalByMatchPlayer(mp1.Id, mp1.Id, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Assistente nao pode ser o mesmo jogador do gol.");
    }

    [Fact]
    public void AddGoalByMatchPlayer_WithUnknownAssist_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();

        var act = () => match.AddGoalByMatchPlayer(mp1.Id, Guid.NewGuid(), null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("O jogador da assistencia nao pertence a esta partida.");
    }

    [Fact]
    public void AddGoalByMatchPlayer_WithAssistFromOtherTeam_ShouldThrow()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers_PostGame();
        // mp1 e mp2 estao em times opostos

        var act = () => match.AddGoalByMatchPlayer(mp1.Id, mp2.Id, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("A assistencia so pode ser de um jogador do mesmo time do autor do gol.");
    }

    [Fact]
    public void AddGoalByMatchPlayer_OwnGoalWithCrossTeamAssist_ShouldBeAllowed()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers_PostGame();

        // Gol contra: assistência de time diferente é permitida
        match.AddGoalByMatchPlayer(mp1.Id, mp2.Id, 10, isOwnGoal: true);

        match.Goals.Should().HaveCount(1);
        // mp1 (time 1) fez gol contra → ponto para o time 2
        match.TeamAGoals.Should().Be(0);
        match.TeamBGoals.Should().Be(1);
    }

    [Fact]
    public void AddGoalByMatchPlayer_WhenFinalized_ShouldBeAllowed()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();
        match.SetScore(0, 0);
        match.FinalizeByVotes();

        match.AddGoalByMatchPlayer(mp1.Id, null, null);

        match.Goals.Should().HaveCount(1);
        match.TeamAGoals.Should().Be(1);
    }

    [Fact]
    public void AddGoalByMatchPlayer_WithSameTeamAssist_ShouldRecordAssist()
    {
        var (match, mps) = CreatePostGameWithFourPlayers();

        // mp0 e mp1 no time 1
        match.AddGoalByMatchPlayer(mps[0].Id, mps[1].Id, 30);

        var goal = match.Goals.Single();
        goal.AssistMatchPlayerId.Should().Be(mps[1].Id);
        match.TeamAGoals.Should().Be(1);
        match.TeamBGoals.Should().Be(0);
    }

    [Fact]
    public void UpdateGoal_WhenGoalNotFound_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();

        var act = () => match.UpdateGoal(Guid.NewGuid(), mp1.Id, null, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Gol nao encontrado.");
    }

    [Fact]
    public void UpdateGoal_WithEmptyScorer_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();
        match.AddGoalByMatchPlayer(mp1.Id, null, null);
        var goalId = match.Goals[0].Id;

        var act = () => match.UpdateGoal(goalId, Guid.Empty, null, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("MatchPlayerId do gol e obrigatorio.");
    }

    [Fact]
    public void UpdateGoal_WithUnknownScorer_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();
        match.AddGoalByMatchPlayer(mp1.Id, null, null);
        var goalId = match.Goals[0].Id;

        var act = () => match.UpdateGoal(goalId, Guid.NewGuid(), null, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("O jogador do gol nao pertence a esta partida.");
    }

    [Fact]
    public void UpdateGoal_WithUnknownAssist_ShouldThrow()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();
        match.AddGoalByMatchPlayer(mp1.Id, null, null);
        var goalId = match.Goals[0].Id;

        var act = () => match.UpdateGoal(goalId, mp1.Id, Guid.NewGuid(), null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("O jogador da assistencia nao pertence a esta partida.");
    }

    [Fact]
    public void UpdateGoal_WithAssistFromOtherTeam_ShouldThrow()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers_PostGame();
        match.AddGoalByMatchPlayer(mp1.Id, null, null);
        var goalId = match.Goals[0].Id;

        var act = () => match.UpdateGoal(goalId, mp1.Id, mp2.Id, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("A assistencia so pode ser de um jogador do mesmo time do autor do gol.");
    }

    [Fact]
    public void UpdateGoal_ChangingScorerToOtherTeam_ShouldRecalculateScore()
    {
        var (match, _, _, mp1, mp2) = CreateMatchWithTwoPlayers_PostGame();
        match.AddGoalByMatchPlayer(mp1.Id, null, 10);
        match.TeamAGoals.Should().Be(1);
        var goalId = match.Goals[0].Id;

        match.UpdateGoal(goalId, mp2.Id, null, 20);

        match.TeamAGoals.Should().Be(0);
        match.TeamBGoals.Should().Be(1);
        match.Goals[0].TimeSeconds.Should().Be(20);
    }

    [Fact]
    public void UpdateGoal_MarkingAsOwnGoal_ShouldInvertScore()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();
        match.AddGoalByMatchPlayer(mp1.Id, null, 10);
        match.TeamAGoals.Should().Be(1);
        var goalId = match.Goals[0].Id;

        match.UpdateGoal(goalId, mp1.Id, null, 10, isOwnGoal: true);

        match.TeamAGoals.Should().Be(0);
        match.TeamBGoals.Should().Be(1);
    }

    [Fact]
    public void RemoveGoal_WithUnknownId_ShouldBeNoOp()
    {
        var (match, _, _, mp1, _) = CreateMatchWithTwoPlayers_PostGame();
        match.AddGoalByMatchPlayer(mp1.Id, null, null);

        match.RemoveGoal(Guid.NewGuid());

        match.Goals.Should().HaveCount(1);
        match.TeamAGoals.Should().Be(1);
    }
}
