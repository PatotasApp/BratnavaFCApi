using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>
/// Testes para funcionalidades adicionadas recentemente ao MatchService:
/// - SetNoShowAsync (DidNotPlay flag)
/// - MatchEntity.ActualStartTime
/// - GetMatchMakingAsync.CanStartMatch / TeamsAssigned
/// - GetAcceptationAsync.CanAdvanceToMatchmaking
/// - GetPostGameAsync.ScoreAAfter / ScoreBAfter
/// - GetPostGameAsync.CanVote / HasVoted
/// </summary>
public sealed class MatchService_NewFeaturesTests
{
    // ── Factories ─────────────────────────────────────────────────────────────

    private static Mock<IRepositoryBase<MatchEntity>> BuildRepoMock(AppDbContext db)
    {
        var repo = new Mock<IRepositoryBase<MatchEntity>>(MockBehavior.Strict);
        repo.Setup(r => r.Add(It.IsAny<MatchEntity>())).Callback<MatchEntity>(m => db.Matches.Add(m));
        repo.Setup(r => r.Remove(It.IsAny<MatchEntity>())).Callback<MatchEntity>(m =>
        {
            if (db.Entry(m).State == EntityState.Detached) db.Matches.Attach(m);
            db.Matches.Remove(m);
        });
        repo.Setup(r => r.Update(It.IsAny<MatchEntity>())).Callback<MatchEntity>(_ => { });
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct => db.SaveChangesAsync(ct));
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, ct) =>
                db.Matches.Include(m => m.Players).ThenInclude(mp => mp.Player)
                    .Include(m => m.Goals).Include(m => m.Votes)
                    .FirstOrDefaultAsync(m => m.Id == id, ct));
        repo.Setup(r => r.GetByIdIncludingInactiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, ct) =>
                db.Matches.Include(m => m.Players).ThenInclude(mp => mp.Player)
                    .Include(m => m.Goals).Include(m => m.Votes)
                    .FirstOrDefaultAsync(m => m.Id == id, ct));
        return repo;
    }

    private static MatchService CreateSut(AppDbContext db, Mock<IRepositoryBase<MatchEntity>> repo)
        => new(db, repo.Object, Mock.Of<IPushService>(), Mock.Of<IReplayUrlService>(),
               Mock.Of<IBetService>(), Mock.Of<INotificationScheduler>(), TestImageStorage.Create());

    // ── Seeds ─────────────────────────────────────────────────────────────────

    private static async Task<GroupEntity> SeedGroupAsync(AppDbContext db)
    {
        var g = new GroupEntity("Test FC", null, Guid.NewGuid());
        db.Groups.Add(g);
        await db.SaveChangesAsync();
        return g;
    }

    /// <summary>Seeds N players with optional UserId for authentication tests.</summary>
    private static async Task<List<PlayerEntity>> SeedPlayersAsync(
        AppDbContext db, Guid groupId, int count, bool withUserId = false)
    {
        var list = new List<PlayerEntity>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(new PlayerEntity(
                $"P{i + 1}",
                userId:       withUserId ? Guid.NewGuid() : null,
                groupId:      groupId,
                skillPoints:  0m,
                isGoalkeeper: false,
                status:       Status.Active));
        }
        db.Players.AddRange(list);
        await db.SaveChangesAsync();
        return list;
    }

    /// <summary>
    /// Cria uma partida com times definidos, opcionalmente na etapa especificada.
    /// Por padrão retorna a partida em MatchMaking com times atribuídos (Team A = p1, Team B = p2).
    /// </summary>
    private static async Task<(MatchEntity match, List<PlayerEntity> players)> SeedMatchWithTeamsAsync(
        AppDbContext db,
        Guid groupId,
        int  playerCount     = 4,
        bool withUserId      = false,
        MatchStatus targetStatus = MatchStatus.MatchMaking)
    {
        var players = await SeedPlayersAsync(db, groupId, playerCount, withUserId);
        var match   = new MatchEntity(groupId, DateTime.UtcNow, "Arena");
        db.Matches.Add(match);

        foreach (var p in players)
        {
            var mp = new MatchPlayerEntity(p.Id);
            match.AddPlayer(mp, p);
        }

        match.OpenAcceptation();
        foreach (var p in players) match.AcceptInvite(p.Id);
        match.GoToMatchMaking();

        if (playerCount >= 2)
        {
            var ids   = players.Select(x => x.Id).ToList();
            var split = ids.Count / 2;
            match.AssignTeams(ids.Take(split).ToList(), ids.Skip(split).ToList());
        }

        if (targetStatus >= MatchStatus.Started) match.Start();
        if (targetStatus >= MatchStatus.Ended)   match.End();
        if (targetStatus >= MatchStatus.PostGame) match.GoToPostGame();
        if (targetStatus >= MatchStatus.Finalized)
        {
            match.SetScore(2, 1);
            match.FinalizeByVotes();
        }

        await db.SaveChangesAsync();
        return (match, players);
    }

    // ==========================================================================
    // SetNoShowAsync
    // ==========================================================================

    [Fact]
    public async Task SetNoShowAsync_WhenPlayerExists_ShouldSetDidNotPlayTrue()
    {
        // Arrange
        await using var db  = DbContextFactory.Create(nameof(SetNoShowAsync_WhenPlayerExists_ShouldSetDidNotPlayTrue));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchWithTeamsAsync(db, grp.Id);

        var mpId = match.Players.First().Id;

        // Act
        var result = await sut.SetNoShowAsync(grp.Id, match.Id, mpId, didNotPlay: true, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var reloaded = await db.Matches
            .Include(m => m.Players)
            .FirstAsync(m => m.Id == match.Id);
        reloaded.Players.First(p => p.Id == mpId).DidNotPlay.Should().BeTrue();
    }

    [Fact]
    public async Task SetNoShowAsync_WhenCalledWithFalse_ShouldClearDidNotPlay()
    {
        // Arrange
        await using var db  = DbContextFactory.Create(nameof(SetNoShowAsync_WhenCalledWithFalse_ShouldClearDidNotPlay));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchWithTeamsAsync(db, grp.Id);

        var mpId = match.Players.First().Id;
        // Marca primeiro como ausente
        await sut.SetNoShowAsync(grp.Id, match.Id, mpId, didNotPlay: true, CancellationToken.None);

        // Act — desfaz
        var result = await sut.SetNoShowAsync(grp.Id, match.Id, mpId, didNotPlay: false, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var reloaded = await db.Matches.Include(m => m.Players).FirstAsync(m => m.Id == match.Id);
        reloaded.Players.First(p => p.Id == mpId).DidNotPlay.Should().BeFalse();
    }

    [Fact]
    public async Task SetNoShowAsync_WhenMatchNotFound_ShouldFail()
    {
        // Arrange
        await using var db  = DbContextFactory.Create(nameof(SetNoShowAsync_WhenMatchNotFound_ShouldFail));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);

        // Act — matchId inexistente
        var result = await sut.SetNoShowAsync(grp.Id, Guid.NewGuid(), Guid.NewGuid(), true, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SetNoShowAsync_WhenMatchPlayerNotFound_ShouldFail()
    {
        // Arrange
        await using var db  = DbContextFactory.Create(nameof(SetNoShowAsync_WhenMatchPlayerNotFound_ShouldFail));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchWithTeamsAsync(db, grp.Id);

        // Act — matchPlayerId inexistente nesta partida
        var result = await sut.SetNoShowAsync(grp.Id, match.Id, Guid.NewGuid(), true, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SetNoShowAsync_ShouldPersistToDatabase()
    {
        // Garante que a alteração sobrevive entre contextos distintos
        await using var db  = DbContextFactory.Create(nameof(SetNoShowAsync_ShouldPersistToDatabase));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchWithTeamsAsync(db, grp.Id);
        var mpId = match.Players.First().Id;

        await sut.SetNoShowAsync(grp.Id, match.Id, mpId, true, CancellationToken.None);

        // Recarrega em novo contexto
        await using var db2 = DbContextFactory.Create(nameof(SetNoShowAsync_ShouldPersistToDatabase));
        var persisted = await db2.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .FirstAsync(m => m.Id == match.Id);

        persisted.Players.First(p => p.Id == mpId).DidNotPlay.Should().BeTrue();
    }

    // ==========================================================================
    // MatchEntity.ActualStartTime
    // ==========================================================================

    // helper local para criar uma partida com dois players em estado MatchMaking com times definidos
    private static (MatchEntity match, PlayerEntity p1, PlayerEntity p2,
                    MatchPlayerEntity mp1, MatchPlayerEntity mp2)
        BuildMatchAtMatchMaking()
    {
        var groupId = Guid.NewGuid();
        var match   = new MatchEntity(groupId, DateTime.UtcNow, "Arena");
        var p1  = new PlayerEntity("A", Guid.NewGuid(), groupId, 0, false, false, Status.Active);
        var p2  = new PlayerEntity("B", Guid.NewGuid(), groupId, 0, false, false, Status.Active);
        var mp1 = new MatchPlayerEntity(p1.Id);
        var mp2 = new MatchPlayerEntity(p2.Id);
        match.AddPlayer(mp1, p1);
        match.AddPlayer(mp2, p2);
        match.OpenAcceptation();
        match.AcceptInvite(p1.Id);
        match.AcceptInvite(p2.Id);
        match.GoToMatchMaking();
        match.AssignTeams(new[] { p1.Id }, new[] { p2.Id });
        return (match, p1, p2, mp1, mp2);
    }

    [Fact]
    public void MatchEntity_Start_ShouldSetActualStartTime()
    {
        // Arrange
        var (match, _, _, _, _) = BuildMatchAtMatchMaking();
        var before = DateTime.UtcNow;

        // Act
        match.Start();

        var after = DateTime.UtcNow;

        // Assert
        match.ActualStartTime.Should().NotBeNull();
        match.ActualStartTime.Should().BeOnOrAfter(before);
        match.ActualStartTime.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void MatchEntity_BeforeStart_ActualStartTime_ShouldBeNull()
    {
        var match = new MatchEntity(Guid.NewGuid(), DateTime.UtcNow, "Arena");
        match.ActualStartTime.Should().BeNull("partida ainda não iniciada.");
    }

    [Fact]
    public void MatchEntity_Start_ShouldNotModifyActualStartTime_OnSubsequentStateChanges()
    {
        // Arrange
        var (match, _, _, _, _) = BuildMatchAtMatchMaking();
        match.Start();
        var startTime = match.ActualStartTime!.Value;

        // Act — encerra e finaliza
        match.End();
        match.GoToPostGame();
        match.SetScore(1, 0);
        match.FinalizeByVotes();

        // Assert
        match.ActualStartTime.Should().Be(startTime,
            "ActualStartTime é imutável após ser definido em Start().");
    }

    // ==========================================================================
    // GetMatchMakingAsync — CanStartMatch / TeamsAssigned
    // ==========================================================================

    [Fact]
    public async Task GetMatchMakingAsync_WhenBothTeamsHavePlayers_CanStartMatchShouldBeTrue()
    {
        // Arrange
        await using var db  = DbContextFactory.Create(nameof(GetMatchMakingAsync_WhenBothTeamsHavePlayers_CanStartMatchShouldBeTrue));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchWithTeamsAsync(db, grp.Id, playerCount: 4);

        // Act
        var result = await sut.GetMatchMakingAsync(grp.Id, match.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.TeamsAssigned.Should().BeTrue();
        result.Data!.CanStartMatch.Should().BeTrue();
    }

    [Fact]
    public async Task GetMatchMakingAsync_WhenNoTeamsAssigned_CanStartMatchShouldBeFalse()
    {
        // Arrange — sem AssignTeams
        await using var db  = DbContextFactory.Create(nameof(GetMatchMakingAsync_WhenNoTeamsAssigned_CanStartMatchShouldBeFalse));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, grp.Id, 4);
        var match   = new MatchEntity(grp.Id, DateTime.UtcNow, "Arena");
        db.Matches.Add(match);
        foreach (var p in players) { var mp = new MatchPlayerEntity(p.Id); match.AddPlayer(mp, p); }
        match.OpenAcceptation();
        foreach (var p in players) match.AcceptInvite(p.Id);
        match.GoToMatchMaking(); // não atribui times
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetMatchMakingAsync(grp.Id, match.Id, CancellationToken.None);

        // Assert
        result.Data!.TeamsAssigned.Should().BeFalse();
        result.Data!.CanStartMatch.Should().BeFalse(
            "sem times definidos, não deve ser possível iniciar a partida.");
    }

    // ==========================================================================
    // GetAcceptationAsync — CanAdvanceToMatchmaking
    // ==========================================================================

    [Fact]
    public async Task GetAcceptationAsync_WhenEnoughPlayersAccepted_CanAdvanceShouldBeTrue()
    {
        // Arrange — MinPlayers=2, MaxPlayers=10, 3 aceitam
        await using var db  = DbContextFactory.Create(nameof(GetAcceptationAsync_WhenEnoughPlayersAccepted_CanAdvanceShouldBeTrue));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        db.GroupSettings.Add(new GroupSettingsEntity(grp.Id, 2, 10, null, null, null));
        var players = await SeedPlayersAsync(db, grp.Id, 3);
        var match   = new MatchEntity(grp.Id, DateTime.UtcNow, "Arena");
        db.Matches.Add(match);
        foreach (var p in players) { var mp = new MatchPlayerEntity(p.Id); match.AddPlayer(mp, p); }
        match.OpenAcceptation();
        foreach (var p in players) match.AcceptInvite(p.Id);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetAcceptationAsync(grp.Id, match.Id, CancellationToken.None);

        // Assert
        result.Data!.CanAdvanceToMatchmaking.Should().BeTrue();
    }

    [Fact]
    public async Task GetAcceptationAsync_WhenTooFewPlayersAccepted_CanAdvanceShouldBeFalse()
    {
        // Arrange — MinPlayers=4, apenas 2 aceitam
        await using var db  = DbContextFactory.Create(nameof(GetAcceptationAsync_WhenTooFewPlayersAccepted_CanAdvanceShouldBeFalse));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        db.GroupSettings.Add(new GroupSettingsEntity(grp.Id, 4, 20, null, null, null));
        var players = await SeedPlayersAsync(db, grp.Id, 4);
        var match   = new MatchEntity(grp.Id, DateTime.UtcNow, "Arena");
        db.Matches.Add(match);
        foreach (var p in players) { var mp = new MatchPlayerEntity(p.Id); match.AddPlayer(mp, p); }
        match.OpenAcceptation();
        // Somente 2 aceitam
        match.AcceptInvite(players[0].Id);
        match.AcceptInvite(players[1].Id);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetAcceptationAsync(grp.Id, match.Id, CancellationToken.None);

        // Assert
        result.Data!.CanAdvanceToMatchmaking.Should().BeFalse(
            "2 aceitos < MinPlayers=4.");
    }

    [Fact]
    public async Task GetAcceptationAsync_WhenAcceptedOverLimit_CanAdvanceShouldBeFalse()
    {
        // Arrange — MaxPlayers=2, 3 aceitam → over limit
        await using var db  = DbContextFactory.Create(nameof(GetAcceptationAsync_WhenAcceptedOverLimit_CanAdvanceShouldBeFalse));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        db.GroupSettings.Add(new GroupSettingsEntity(grp.Id, 2, 2, null, null, null));
        var players = await SeedPlayersAsync(db, grp.Id, 3);
        var match   = new MatchEntity(grp.Id, DateTime.UtcNow, "Arena");
        db.Matches.Add(match);
        foreach (var p in players) { var mp = new MatchPlayerEntity(p.Id); match.AddPlayer(mp, p); }
        match.OpenAcceptation();
        foreach (var p in players) match.AcceptInvite(p.Id);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetAcceptationAsync(grp.Id, match.Id, CancellationToken.None);

        // Assert
        result.Data!.AcceptedOverLimit.Should().BeTrue();
        result.Data!.CanAdvanceToMatchmaking.Should().BeFalse(
            "over limit impede avanço independente do número mínimo.");
    }

    [Fact]
    public async Task GetAcceptationAsync_WhenNoGroupSettings_FallsBackToMinimumOfTwo()
    {
        // Arrange — sem GroupSettings: deve usar mínimo de 2
        await using var db  = DbContextFactory.Create(nameof(GetAcceptationAsync_WhenNoGroupSettings_FallsBackToMinimumOfTwo));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        // SEM GroupSettings
        var players = await SeedPlayersAsync(db, grp.Id, 2);
        var match   = new MatchEntity(grp.Id, DateTime.UtcNow, "Arena");
        db.Matches.Add(match);
        foreach (var p in players) { var mp = new MatchPlayerEntity(p.Id); match.AddPlayer(mp, p); }
        match.OpenAcceptation();
        foreach (var p in players) match.AcceptInvite(p.Id);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetAcceptationAsync(grp.Id, match.Id, CancellationToken.None);

        // Assert
        result.Data!.CanAdvanceToMatchmaking.Should().BeTrue(
            "2 aceitos e sem configuração de grupo → mínimo padrão de 2 é satisfeito.");
    }

    // ==========================================================================
    // GetPostGameAsync — ScoreAAfter / ScoreBAfter
    // ==========================================================================

    [Fact]
    public async Task GetPostGameAsync_Goals_ShouldHaveAccumulatedScoreAfterEachGoal()
    {
        // Arrange
        await using var db  = DbContextFactory.Create(nameof(GetPostGameAsync_Goals_ShouldHaveAccumulatedScoreAfterEachGoal));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchWithTeamsAsync(db, grp.Id, playerCount: 4,
            targetStatus: MatchStatus.PostGame);

        // Time A = jogadores 0,1 | Time B = jogadores 2,3
        var teamAMp = match.Players.First(p => p.Team == 1);
        var teamBMp = match.Players.First(p => p.Team == 2);

        // Adiciona gols: A marca → B marca → A marca
        match.AddGoalByMatchPlayer(teamAMp.Id, null, timeSeconds: 10);
        match.AddGoalByMatchPlayer(teamBMp.Id, null, timeSeconds: 20);
        match.AddGoalByMatchPlayer(teamAMp.Id, null, timeSeconds: 30);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetPostGameAsync(grp.Id, match.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var goals = result.Data!.Goals.OrderBy(g => g.TimeSeconds).ToList();
        goals.Should().HaveCount(3);

        goals[0].ScoreAAfter.Should().Be(1); goals[0].ScoreBAfter.Should().Be(0); // 1-0
        goals[1].ScoreAAfter.Should().Be(1); goals[1].ScoreBAfter.Should().Be(1); // 1-1
        goals[2].ScoreAAfter.Should().Be(2); goals[2].ScoreBAfter.Should().Be(1); // 2-1
    }

    [Fact]
    public async Task GetPostGameAsync_OwnGoal_ShouldIncrementOpponentScore()
    {
        // Arrange — jogador do Time A marca gol contra (ponto vai para Time B)
        await using var db  = DbContextFactory.Create(nameof(GetPostGameAsync_OwnGoal_ShouldIncrementOpponentScore));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchWithTeamsAsync(db, grp.Id, playerCount: 4,
            targetStatus: MatchStatus.PostGame);

        var teamAMp = match.Players.First(p => p.Team == 1);
        match.AddGoalByMatchPlayer(teamAMp.Id, null, timeSeconds: 15, isOwnGoal: true);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetPostGameAsync(grp.Id, match.Id, CancellationToken.None);

        // Assert
        var goal = result.Data!.Goals.Single();
        goal.IsOwnGoal.Should().BeTrue();
        goal.ScoreAAfter.Should().Be(0, "gol contra de jogador do Time A não pontua para A.");
        goal.ScoreBAfter.Should().Be(1, "gol contra de jogador do Time A pontua para B.");
    }

    [Fact]
    public async Task GetPostGameAsync_NoGoals_ShouldReturnEmptyGoalsList()
    {
        // Arrange
        await using var db  = DbContextFactory.Create(nameof(GetPostGameAsync_NoGoals_ShouldReturnEmptyGoalsList));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchWithTeamsAsync(db, grp.Id, targetStatus: MatchStatus.PostGame);

        // Act
        var result = await sut.GetPostGameAsync(grp.Id, match.Id, CancellationToken.None);

        // Assert
        result.Data!.Goals.Should().BeEmpty();
    }

    // ==========================================================================
    // GetPostGameAsync — CanVote / HasVoted
    // ==========================================================================

    [Fact]
    public async Task GetPostGameAsync_WhenUserHasNotVoted_CanVoteShouldBeTrue()
    {
        // Arrange — jogador autenticado ainda não votou
        await using var db  = DbContextFactory.Create(nameof(GetPostGameAsync_WhenUserHasNotVoted_CanVoteShouldBeTrue));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        // withUserId = true para ter UserId no PlayerEntity
        var (match, players) = await SeedMatchWithTeamsAsync(db, grp.Id, playerCount: 4,
            withUserId: true, targetStatus: MatchStatus.PostGame);

        // Recarrega players para obter UserId
        var dbPlayers = await db.Players.AsNoTracking()
            .Where(p => p.GroupId == grp.Id).ToListAsync();

        var voter = dbPlayers.First(p => p.UserId.HasValue);

        // Act — passa o userId de um jogador que ainda não votou
        var result = await sut.GetPostGameAsync(grp.Id, match.Id, CancellationToken.None,
            requestingUserId: voter.UserId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.CanVote.Should().BeTrue("jogador não-convidado, em time, sem DidNotPlay e sem voto.");
        result.Data!.HasVoted.Should().BeFalse();
        result.Data!.MyVotedForMatchPlayerId.Should().BeNull();
    }

    [Fact]
    public async Task GetPostGameAsync_WhenUserHasVoted_CanVoteShouldBeFalse_HasVotedTrue()
    {
        // Arrange
        await using var db  = DbContextFactory.Create(nameof(GetPostGameAsync_WhenUserHasVoted_CanVoteShouldBeFalse_HasVotedTrue));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchWithTeamsAsync(db, grp.Id, playerCount: 4,
            withUserId: true, targetStatus: MatchStatus.PostGame);

        var dbPlayers = await db.Players.AsNoTracking().Where(p => p.GroupId == grp.Id).ToListAsync();
        var voter    = dbPlayers.First(p => p.UserId.HasValue);
        var voterMp  = match.Players.First(p => p.PlayerId == voter.Id);
        var targetMp = match.Players.First(p => p.PlayerId != voter.Id && (p.Team == 1 || p.Team == 2));

        // Registra voto via service (garante tracking correto do EF)
        await sut.VoteAsync(grp.Id, match.Id, voterMp.Id, targetMp.Id, CancellationToken.None);

        // Act
        var result = await sut.GetPostGameAsync(grp.Id, match.Id, CancellationToken.None,
            requestingUserId: voter.UserId);

        // Assert
        result.Data!.CanVote.Should().BeFalse("já votou.");
        result.Data!.HasVoted.Should().BeTrue();
        result.Data!.MyVotedForMatchPlayerId.Should().Be(targetMp.Id);
    }

    [Fact]
    public async Task GetPostGameAsync_WhenPlayerMarkedDidNotPlay_CanVoteShouldBeFalse()
    {
        // Arrange — jogador marcado como não foi jogar
        await using var db  = DbContextFactory.Create(nameof(GetPostGameAsync_WhenPlayerMarkedDidNotPlay_CanVoteShouldBeFalse));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchWithTeamsAsync(db, grp.Id, playerCount: 4,
            withUserId: true, targetStatus: MatchStatus.PostGame);

        var dbPlayers = await db.Players.AsNoTracking().Where(p => p.GroupId == grp.Id).ToListAsync();
        var player    = dbPlayers.First(p => p.UserId.HasValue);
        var mp        = match.Players.First(p => p.PlayerId == player.Id);

        // Marca como não foi
        mp.SetDidNotPlay(true);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetPostGameAsync(grp.Id, match.Id, CancellationToken.None,
            requestingUserId: player.UserId);

        // Assert
        result.Data!.CanVote.Should().BeFalse("jogador marcado como DidNotPlay não pode votar.");
    }

    [Fact]
    public async Task GetPostGameAsync_WhenRequestingUserNotInMatch_CanVoteShouldBeNull()
    {
        // Arrange — usuário não tem jogador nesta partida
        await using var db  = DbContextFactory.Create(nameof(GetPostGameAsync_WhenRequestingUserNotInMatch_CanVoteShouldBeNull));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchWithTeamsAsync(db, grp.Id, targetStatus: MatchStatus.PostGame);

        // Act — userId totalmente alheio à partida
        var result = await sut.GetPostGameAsync(grp.Id, match.Id, CancellationToken.None,
            requestingUserId: Guid.NewGuid());

        // Assert
        result.Data!.CanVote.Should().BeNull("usuário sem jogador na partida → null.");
        result.Data!.HasVoted.Should().BeNull();
    }

    [Fact]
    public async Task GetPostGameAsync_WhenNoRequestingUser_CanVoteShouldBeNull()
    {
        // Arrange
        await using var db  = DbContextFactory.Create(nameof(GetPostGameAsync_WhenNoRequestingUser_CanVoteShouldBeNull));
        var repo = BuildRepoMock(db);
        var sut  = CreateSut(db, repo);
        var grp  = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchWithTeamsAsync(db, grp.Id, targetStatus: MatchStatus.PostGame);

        // Act — sem userId (chamada anônima)
        var result = await sut.GetPostGameAsync(grp.Id, match.Id, CancellationToken.None,
            requestingUserId: null);

        // Assert
        result.Data!.CanVote.Should().BeNull();
        result.Data!.HasVoted.Should().BeNull();
    }
}
