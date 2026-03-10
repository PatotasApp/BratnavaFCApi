using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace BranavaFC.Tests;

public sealed class MatchServiceTests
{
    private static Mock<IRepositoryBase<MatchEntity>> BuildRepoMock(AppDbContext db)
    {
        var repo = new Mock<IRepositoryBase<MatchEntity>>(MockBehavior.Strict);

        repo.Setup(r => r.Add(It.IsAny<MatchEntity>()))
            .Callback<MatchEntity>(m => db.Matches.Add(m));

        repo.Setup(r => r.Remove(It.IsAny<MatchEntity>()))
            .Callback<MatchEntity>(m =>
            {
                if (db.Entry(m).State == EntityState.Detached)
                    db.Matches.Attach(m);

                db.Matches.Remove(m);
            });

        repo.Setup(r => r.Update(It.IsAny<MatchEntity>()))
            .Callback<MatchEntity>(_ => { });

        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct => db.SaveChangesAsync(ct));

        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, ct) =>
                db.Matches
                    .Include(m => m.Players).ThenInclude(mp => mp.Player)
                    .Include(m => m.Goals)
                    .Include(m => m.Votes)
                    .FirstOrDefaultAsync(m => m.Id == id, ct));

        repo.Setup(r => r.GetByIdIncludingInactiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, ct) =>
                db.Matches
                    .Include(m => m.Players).ThenInclude(mp => mp.Player)
                    .Include(m => m.Goals)
                    .Include(m => m.Votes)
                    .FirstOrDefaultAsync(m => m.Id == id, ct));

        return repo;
    }

    private static MatchService CreateSut(AppDbContext db, Mock<IRepositoryBase<MatchEntity>> repo)
        => new(db, repo.Object);

    // =========================
    // Seeds
    // =========================
    private static async Task<GroupEntity> SeedGroupAsync(AppDbContext db, string name = "Bratnava FC")
    {
        var group = new GroupEntity(name, null);
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task<List<PlayerEntity>> SeedPlayersAsync(AppDbContext db, Guid groupId, int count)
    {
        var list = new List<PlayerEntity>(count);

        for (int i = 0; i < count; i++)
        {
            list.Add(new PlayerEntity(
                name: $"P{i + 1}",
                userId: Guid.NewGuid(),
                groupId: groupId,
                skillPoints: 0m,
                isGoalkeeper: false,
                status: Status.Active));
        }

        db.Players.AddRange(list);
        await db.SaveChangesAsync();
        return list;
    }

    private static async Task<(MatchEntity match, List<PlayerEntity> players)> SeedMatchAsync(
        AppDbContext db,
        Guid groupId,
        int playersCount,
        MatchStatus targetStatus,
        bool acceptAllInvites = true,
        bool defineTeamsIfPossible = true,
        bool setScoreInPostGame = false,
        (int a, int b)? score = null,
        DateTime? playedAtUtc = null)
    {
        var players = await SeedPlayersAsync(db, groupId, playersCount);

        var match = new MatchEntity(groupId, playedAtUtc ?? DateTime.UtcNow, "Boca Jrs");
        db.Matches.Add(match);

        // Add players only allowed in Created
        foreach (var p in players)
        {
            var mp = new MatchPlayerEntity(p.Id);
            match.AddPlayer(mp, p);
        }

        await db.SaveChangesAsync();

        // Flow: Created -> Acceptation
        if (targetStatus >= MatchStatus.Acceptation)
        {
            match.OpenAcceptation();

            if (acceptAllInvites)
            {
                foreach (var p in players)
                    match.AcceptInvite(p.Id);
            }
        }

        // Acceptation -> MatchMaking
        if (targetStatus >= MatchStatus.MatchMaking)
        {
            match.GoToMatchMaking();

            if (defineTeamsIfPossible)
            {
                if (players.Count < 2)
                    defineTeamsIfPossible = false;
                else
                {
                    var ids = players.Select(p => p.Id).ToList();
                    var split = Math.Max(1, ids.Count / 2);
                    var teamA = ids.Take(split).ToList();
                    var teamB = ids.Skip(split).ToList();
                    if (teamB.Count == 0)
                    {
                        teamB.Add(teamA.Last());
                        teamA.RemoveAt(teamA.Count - 1);
                    }

                    match.AssignTeams(teamA, teamB);
                }
            }
        }

        // MatchMaking -> Started
        if (targetStatus >= MatchStatus.Started)
            match.Start();

        // Started -> Ended
        if (targetStatus >= MatchStatus.Ended)
            match.End();

        // Ended -> PostGame
        if (targetStatus >= MatchStatus.PostGame)
            match.GoToPostGame();

        // PostGame -> score (optional)
        if (setScoreInPostGame)
        {
            if (match.Status != MatchStatus.PostGame)
                throw new InvalidOperationException("Seed pediu score, mas match não está em PostGame.");

            var (a, b) = score ?? (1, 0);
            match.SetScore(a, b);
        }

        // PostGame -> Finalized (se pediu)
        if (targetStatus >= MatchStatus.Finalized)
        {
            if (match.Status != MatchStatus.PostGame)
                throw new InvalidOperationException("Seed pediu Finalized, mas match não está em PostGame.");

            if (!match.TeamAGoals.HasValue || !match.TeamBGoals.HasValue)
                match.SetScore(1, 0);

            match.FinalizeByVotes();
        }

        await db.SaveChangesAsync();
        return (match, players);
    }

    // =========================
    // CREATE + ACCEPTATION
    // =========================

    [Fact]
    public async Task Create_WhenValid_ShouldSyncPlayers_AndOpenAcceptation()
    {
        await using var db = DbContextFactory.Create(nameof(Create_WhenValid_ShouldSyncPlayers_AndOpenAcceptation));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 3);

        var match = new MatchEntity(group.Id, DateTime.UtcNow, "Boca Jrs");

        await sut.Create(group.Id, match, CancellationToken.None);

        var reloaded = await db.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .FirstAsync(m => m.Id == match.Id);

        reloaded.Status.Should().Be(MatchStatus.Acceptation);
        reloaded.Players.Should().HaveCount(players.Count);
        reloaded.Players.All(p => p.Team == 0).Should().BeTrue();
    }

    [Fact]
    public async Task Create_WhenAlreadyExistsNonFinalizedMatch_ShouldThrow_AndNotCreateNew()
    {
        await using var db = DbContextFactory.Create(nameof(Create_WhenAlreadyExistsNonFinalizedMatch_ShouldThrow_AndNotCreateNew));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        // existe uma partida "em andamento" (!= Finalized)
        await SeedMatchAsync(db, group.Id, playersCount: 2, targetStatus: MatchStatus.Acceptation, acceptAllInvites: false);

        var beforeCount = await db.Matches.CountAsync();

        var newMatch = new MatchEntity(group.Id, DateTime.UtcNow, "Boca Jrs");

        var act = async () => await sut.Create(group.Id, newMatch, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Ja existe uma partida em andamento (não finalizada) para este grupo.");

        var afterCount = await db.Matches.CountAsync();
        afterCount.Should().Be(beforeCount);
    }

    [Fact]
    public async Task Create_WhenOnlyFinalizedMatchesExist_ShouldAllowCreating()
    {
        await using var db = DbContextFactory.Create(nameof(Create_WhenOnlyFinalizedMatchesExist_ShouldAllowCreating));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        // cria uma Finalized
        await SeedMatchAsync(db, group.Id, playersCount: 2, targetStatus: MatchStatus.Finalized, acceptAllInvites: true, defineTeamsIfPossible: true, setScoreInPostGame: true);

        var newMatch = new MatchEntity(group.Id, DateTime.UtcNow, "Boca Jrs");

        await sut.Create(group.Id, newMatch, CancellationToken.None);

        var reloaded = await db.Matches
            .AsNoTracking()
            .FirstAsync(m => m.Id == newMatch.Id);

        reloaded.Status.Should().Be(MatchStatus.Acceptation);
    }

    [Fact]
    public async Task AcceptInviteAsync_WhenInAcceptation_ShouldSetInviteResponseAccepted()
    {
        await using var db = DbContextFactory.Create(nameof(AcceptInviteAsync_WhenInAcceptation_ShouldSetInviteResponseAccepted));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.Acceptation,
            acceptAllInvites: false);

        var p1 = players[0];

        await sut.AcceptInviteAsync(group.Id, match.Id, p1.Id, CancellationToken.None);

        var reloaded = await db.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .FirstAsync(m => m.Id == match.Id);

        var mp = reloaded.Players.First(x => x.PlayerId == p1.Id);
        mp.InviteResponse.Should().Be(InviteResponse.Accepted);
    }

    [Fact]
    public async Task RejectInviteAsync_WhenInAcceptation_ShouldSetInviteResponseRejected()
    {
        await using var db = DbContextFactory.Create(nameof(RejectInviteAsync_WhenInAcceptation_ShouldSetInviteResponseRejected));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.Acceptation,
            acceptAllInvites: false);

        var p1 = players[0];

        await sut.RejectInviteAsync(group.Id, match.Id, p1.Id, CancellationToken.None);

        var reloaded = await db.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .FirstAsync(m => m.Id == match.Id);

        var mp = reloaded.Players.First(x => x.PlayerId == p1.Id);
        mp.InviteResponse.Should().Be(InviteResponse.Rejected);
    }

    [Fact]
    public async Task AcceptInviteAsync_WhenNotAcceptation_ShouldThrow()
    {
        await using var db = DbContextFactory.Create(nameof(AcceptInviteAsync_WhenNotAcceptation_ShouldThrow));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.MatchMaking,
            acceptAllInvites: true);

        var act = async () => await sut.AcceptInviteAsync(group.Id, match.Id, players[0].Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("So e possivel aceitar convite quando a partida esta em Acceptation.");
    }

    // =========================
    // CURRENT MATCH (new)
    // =========================

    [Fact]
    public async Task GetCurrentAsync_WhenNone_ShouldReturnNull()
    {
        await using var db = DbContextFactory.Create(nameof(GetCurrentAsync_WhenNone_ShouldReturnNull));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        var current = await sut.GetCurrentAsync(group.Id, CancellationToken.None);

        current.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentAsync_WhenOnlyFinalized_ShouldReturnNull()
    {
        await using var db = DbContextFactory.Create(nameof(GetCurrentAsync_WhenOnlyFinalized_ShouldReturnNull));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        await SeedMatchAsync(db, group.Id, playersCount: 2, targetStatus: MatchStatus.Finalized, acceptAllInvites: true, defineTeamsIfPossible: true, setScoreInPostGame: true);

        var current = await sut.GetCurrentAsync(group.Id, CancellationToken.None);

        current.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentAsync_WhenHasNonFinalized_ShouldReturnLatestByPlayedAt()
    {
        await using var db = DbContextFactory.Create(nameof(GetCurrentAsync_WhenHasNonFinalized_ShouldReturnLatestByPlayedAt));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        // mais antiga (não finalizada)
        var (m1, _) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.Acceptation,
            acceptAllInvites: false,
            playedAtUtc: DateTime.UtcNow.AddHours(-2));

        // mais recente (não finalizada)
        var (m2, _) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.MatchMaking,
            acceptAllInvites: true,
            defineTeamsIfPossible: true,
            playedAtUtc: DateTime.UtcNow.AddHours(-1));

        var current = await sut.GetCurrentAsync(group.Id, CancellationToken.None);

        current.Should().NotBeNull();
        current!.Id.Should().Be(m2.Id);
        current.Status.Should().NotBe(MatchStatus.Finalized);
    }

    // =========================
    // MATCHMAKING
    // =========================

    [Fact]
    public async Task GoToMatchMakingAsync_WhenLessThan2Accepted_ShouldThrow()
    {
        await using var db = DbContextFactory.Create(nameof(GoToMatchMakingAsync_WhenLessThan2Accepted_ShouldThrow));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.Acceptation,
            acceptAllInvites: false);

        await sut.AcceptInviteAsync(group.Id, match.Id, players[0].Id, CancellationToken.None);

        var act = async () => await sut.GoToMatchMakingAsync(group.Id, match.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Precisa de ao menos 2 jogadores aceitos para gerar times.");
    }

    [Fact]
    public async Task GoToMatchMakingAsync_WhenOk_ShouldMoveToMatchMaking()
    {
        await using var db = DbContextFactory.Create(nameof(GoToMatchMakingAsync_WhenOk_ShouldMoveToMatchMaking));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        var (match, _) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 3,
            targetStatus: MatchStatus.Acceptation,
            acceptAllInvites: true);

        await sut.GoToMatchMakingAsync(group.Id, match.Id, CancellationToken.None);

        var reloaded = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        reloaded.Status.Should().Be(MatchStatus.MatchMaking);
    }

    [Fact]
    public async Task AssignTeamsAsync_WhenNotMatchMaking_ShouldThrow()
    {
        await using var db = DbContextFactory.Create(nameof(AssignTeamsAsync_WhenNotMatchMaking_ShouldThrow));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.Acceptation,
            acceptAllInvites: true);

        var dto = new AssignTeamsDto
        {
            TeamAMatchPlayerIds = new List<Guid> { players[0].Id },
            TeamBMatchPlayerIds = new List<Guid> { players[1].Id },
        };

        var act = async () => await sut.AssignTeamsAsync(group.Id, match.Id, dto, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("So e possivel atribuir times quando a partida esta em MatchMaking.");
    }

    [Fact]
    public async Task AssignTeamsAsync_WhenMatchMaking_ShouldAssignTeams_ByPlayerIdLists()
    {
        await using var db = DbContextFactory.Create(nameof(AssignTeamsAsync_WhenMatchMaking_ShouldAssignTeams_ByPlayerIdLists));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 4,
            targetStatus: MatchStatus.MatchMaking,
            acceptAllInvites: true,
            defineTeamsIfPossible: false);

        var dto = new AssignTeamsDto
        {
            TeamAMatchPlayerIds = new List<Guid> { players[0].Id, players[1].Id },
            TeamBMatchPlayerIds = new List<Guid> { players[2].Id, players[3].Id },
        };

        await sut.AssignTeamsAsync(group.Id, match.Id, dto, CancellationToken.None);

        var reloaded = await db.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .FirstAsync(m => m.Id == match.Id);

        reloaded.Players.Count(p => p.Team == 1).Should().Be(2);
        reloaded.Players.Count(p => p.Team == 2).Should().Be(2);
    }

    [Fact]
    public async Task SwapPlayersByPlayerIdAsync_WhenMatchMaking_ShouldSwap()
    {
        await using var db = DbContextFactory.Create(nameof(SwapPlayersByPlayerIdAsync_WhenMatchMaking_ShouldSwap));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.MatchMaking,
            acceptAllInvites: true,
            defineTeamsIfPossible: true);

        var before = await db.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .FirstAsync(m => m.Id == match.Id);

        var pA = players[0];
        var pB = players[1];

        var teamA_before = before.Players.Single(p => p.PlayerId == pA.Id).Team;
        var teamB_before = before.Players.Single(p => p.PlayerId == pB.Id).Team;
        teamA_before.Should().NotBe(teamB_before);

        await sut.SwapPlayersByPlayerIdAsync(group.Id, match.Id, pA.Id, pB.Id, CancellationToken.None);

        var after = await db.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .FirstAsync(m => m.Id == match.Id);

        after.Players.Single(p => p.PlayerId == pA.Id).Team.Should().Be(teamB_before);
        after.Players.Single(p => p.PlayerId == pB.Id).Team.Should().Be(teamA_before);
    }

    // =========================
    // START / END / POSTGAME
    // =========================

    [Fact]
    public async Task StartMatchAsync_WhenNotMatchMaking_ShouldThrow()
    {
        await using var db = DbContextFactory.Create(nameof(StartMatchAsync_WhenNotMatchMaking_ShouldThrow));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: true);

        var act = async () => await sut.StartMatchAsync(group.Id, match.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("A partida so pode ser iniciada se estiver em MatchMaking.");
    }

    [Fact]
    public async Task StartMatchAsync_WhenMatchMakingWithTeams_ShouldMoveToStarted()
    {
        await using var db = DbContextFactory.Create(nameof(StartMatchAsync_WhenMatchMakingWithTeams_ShouldMoveToStarted));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.MatchMaking, acceptAllInvites: true, defineTeamsIfPossible: true);

        await sut.StartMatchAsync(group.Id, match.Id, CancellationToken.None);

        var reloaded = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        reloaded.Status.Should().Be(MatchStatus.Started);
    }

    [Fact]
    public async Task EndMatchAsync_WhenStarted_ShouldMoveToEnded()
    {
        await using var db = DbContextFactory.Create(nameof(EndMatchAsync_WhenStarted_ShouldMoveToEnded));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Started, acceptAllInvites: true, defineTeamsIfPossible: true);

        await sut.EndMatchAsync(group.Id, match.Id, CancellationToken.None);

        var reloaded = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        reloaded.Status.Should().Be(MatchStatus.Ended);
    }

    [Fact]
    public async Task GoToPostGameAsync_WhenEnded_ShouldMoveToPostGame()
    {
        await using var db = DbContextFactory.Create(nameof(GoToPostGameAsync_WhenEnded_ShouldMoveToPostGame));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Ended, acceptAllInvites: true, defineTeamsIfPossible: true);

        await sut.GoToPostGameAsync(group.Id, match.Id, CancellationToken.None);

        var reloaded = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        reloaded.Status.Should().Be(MatchStatus.PostGame);
    }

    // =========================
    // GOALS (Started/PostGame)
    // =========================

    [Fact]
    public async Task AddGoalAsync_WhenStarted_ShouldAddGoal_AndRecalculateScore()
    {
        await using var db = DbContextFactory.Create(nameof(AddGoalAsync_WhenStarted_ShouldAddGoal_AndRecalculateScore));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.Started,
            acceptAllInvites: true,
            defineTeamsIfPossible: true);

        await sut.AddGoalAsync(
            group.Id,
            match.Id,
            new AddGoalRequestDto(ScorerPlayerId: players[0].Id, AssistPlayerId: null, Time: "12:34"),
            CancellationToken.None);

        var reloaded = await db.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .Include(m => m.Goals)
            .FirstAsync(m => m.Id == match.Id);

        reloaded.Goals.Should().HaveCount(1);
        reloaded.TeamAGoals.Should().NotBeNull();
        reloaded.TeamBGoals.Should().NotBeNull();
        (reloaded.TeamAGoals!.Value + reloaded.TeamBGoals!.Value).Should().Be(1);
    }

    [Fact]
    public async Task AddGoalAsync_WhenPostGame_ShouldAllow()
    {
        await using var db = DbContextFactory.Create(nameof(AddGoalAsync_WhenPostGame_ShouldAllow));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 3,
            targetStatus: MatchStatus.PostGame,
            acceptAllInvites: true,
            defineTeamsIfPossible: true);

        await sut.AddGoalAsync(
            group.Id,
            match.Id,
            new AddGoalRequestDto(players[0].Id, null, "00:10"),
            CancellationToken.None);

        var reloaded = await db.Matches
            .AsNoTracking()
            .Include(m => m.Goals)
            .FirstAsync(m => m.Id == match.Id);

        reloaded.Goals.Should().HaveCount(1);
    }

    [Fact]
    public async Task RemoveGoalAsync_WhenExists_ShouldRemove_AndUpdateScore()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveGoalAsync_WhenExists_ShouldRemove_AndUpdateScore));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.PostGame,
            acceptAllInvites: true,
            defineTeamsIfPossible: true);

        await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[0].Id, null, "00:10"), CancellationToken.None);
        await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[0].Id, null, "00:20"), CancellationToken.None);

        var tracked = await db.Matches
            .Include(m => m.Goals)
            .FirstAsync(m => m.Id == match.Id);

        var goalId = tracked.Goals[0].Id;

        db.ChangeTracker.Clear();

        await sut.RemoveGoalAsync(group.Id, match.Id, goalId, CancellationToken.None);

        var reloaded = await db.Matches
            .AsNoTracking()
            .Include(m => m.Goals)
            .FirstAsync(m => m.Id == match.Id);

        reloaded.Goals.Should().HaveCount(1);
        (reloaded.TeamAGoals!.Value + reloaded.TeamBGoals!.Value).Should().Be(1);
    }

    [Fact]
    public async Task AddGoalsBulkAsync_WhenValid_ShouldAddAllGoals_AndCommit()
    {
        await using var db = DbContextFactory.Create(nameof(AddGoalsBulkAsync_WhenValid_ShouldAddAllGoals_AndCommit));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 4,
            targetStatus: MatchStatus.PostGame,
            acceptAllInvites: true,
            defineTeamsIfPossible: true);

        var goals = new List<AddGoalRequestDto>
        {
            new(players[0].Id, null, "00:05"),
            new(players[1].Id, players[0].Id, "00:10"),
            new(players[2].Id, null, null),
        };

        var dto = new AddGoalsBulkRequestDto(goals);

        await sut.AddGoalsBulkAsync(group.Id, match.Id, dto, CancellationToken.None);

        var reloaded = await db.Matches
            .AsNoTracking()
            .Include(m => m.Goals)
            .FirstAsync(m => m.Id == match.Id);

        reloaded.Goals.Should().HaveCount(3);
        (reloaded.TeamAGoals!.Value + reloaded.TeamBGoals!.Value).Should().Be(3);
    }

    [Fact]
    public async Task AddGoalsBulkAsync_WhenOneInvalid_ShouldRollbackAndAddNone()
    {
        await using var db = DbContextFactory.Create(nameof(AddGoalsBulkAsync_WhenOneInvalid_ShouldRollbackAndAddNone));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.PostGame,
            acceptAllInvites: true,
            defineTeamsIfPossible: true);

        var goals = new List<AddGoalRequestDto>
        {
            new(players[0].Id, null, "00:05"),
            new(Guid.NewGuid(), null, "00:06"),
        };

        var dto = new AddGoalsBulkRequestDto(goals);

        var act = async () => await sut.AddGoalsBulkAsync(group.Id, match.Id, dto, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("O jogador do gol nao pertence a esta partida.");

        var reloaded = await db.Matches
            .AsNoTracking()
            .Include(m => m.Goals)
            .FirstAsync(m => m.Id == match.Id);

        reloaded.Goals.Should().BeEmpty();
        reloaded.TeamAGoals.Should().BeNull();
        reloaded.TeamBGoals.Should().BeNull();
    }

    // =========================
    // SCORE / VOTE / FINALIZE
    // =========================

    [Fact]
    public async Task SetScoreAsync_WhenNotPostGame_ShouldThrow()
    {
        await using var db = DbContextFactory.Create(nameof(SetScoreAsync_WhenNotPostGame_ShouldThrow));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Ended, acceptAllInvites: true, defineTeamsIfPossible: true);

        var act = async () => await sut.SetScoreAsync(group.Id, match.Id, 1, 0, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("So e possivel setar placar quando a partida esta em PostGame.");
    }

    [Fact]
    public async Task VoteAsync_WhenPostGame_ShouldCreateVote_AndPreventDoubleVote()
    {
        await using var db = DbContextFactory.Create(nameof(VoteAsync_WhenPostGame_ShouldCreateVote_AndPreventDoubleVote));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 3,
            targetStatus: MatchStatus.PostGame,
            acceptAllInvites: true,
            defineTeamsIfPossible: true,
            setScoreInPostGame: true,
            score: (1, 0));

        var tracked = await db.Matches
            .Include(m => m.Players)
            .Include(m => m.Votes)
            .FirstAsync(m => m.Id == match.Id);

        var voter = tracked.Players.First(p => p.PlayerId == players[0].Id);
        var voted = tracked.Players.First(p => p.PlayerId == players[1].Id);

        await sut.VoteAsync(group.Id, match.Id, voter.Id, voted.Id, CancellationToken.None);

        var act2 = async () => await sut.VoteAsync(group.Id, match.Id, voter.Id, voted.Id, CancellationToken.None);

        await act2.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Esse jogador ja votou.");
    }

    [Fact]
    public async Task FinalizeMatchAsync_WhenPostGameWithScore_ShouldFinalize()
    {
        await using var db = DbContextFactory.Create(nameof(FinalizeMatchAsync_WhenPostGameWithScore_ShouldFinalize));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.PostGame,
            acceptAllInvites: true,
            defineTeamsIfPossible: true,
            setScoreInPostGame: true,
            score: (2, 1));

        await sut.FinalizeMatchAsync(group.Id, match.Id, CancellationToken.None);

        var reloaded = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        reloaded.Status.Should().Be(MatchStatus.Finalized);
        reloaded.TeamAGoals.Should().Be(2);
        reloaded.TeamBGoals.Should().Be(1);
    }

    [Fact]
    public async Task FinalizeMatchAsync_WhenPostGameNoScoreButHasGoals_ShouldRecalculateAndFinalize()
    {
        await using var db = DbContextFactory.Create(nameof(FinalizeMatchAsync_WhenPostGameNoScoreButHasGoals_ShouldRecalculateAndFinalize));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.PostGame,
            acceptAllInvites: true,
            defineTeamsIfPossible: true,
            setScoreInPostGame: false);

        await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[0].Id, null, "00:01"), CancellationToken.None);
        await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[0].Id, null, "00:02"), CancellationToken.None);
        await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[1].Id, null, "00:03"), CancellationToken.None);

        await sut.FinalizeMatchAsync(group.Id, match.Id, CancellationToken.None);

        var reloaded = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        reloaded.Status.Should().Be(MatchStatus.Finalized);
        (reloaded.TeamAGoals!.Value + reloaded.TeamBGoals!.Value).Should().Be(3);
    }

    // =========================
    // GET GOALS (DTO mapping/order)
    // =========================

    [Fact]
    public async Task GetGoalsAsync_ShouldOrderByTime_NullsLast_AndMapNames()
    {
        await using var db = DbContextFactory.Create(nameof(GetGoalsAsync_ShouldOrderByTime_NullsLast_AndMapNames));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 3,
            targetStatus: MatchStatus.PostGame,
            acceptAllInvites: true,
            defineTeamsIfPossible: true,
            setScoreInPostGame: true,
            score: (0, 0));

        await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[0].Id, null, "00:20"), CancellationToken.None);
        await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[1].Id, null, null), CancellationToken.None);
        await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[2].Id, null, "00:10"), CancellationToken.None);

        var goals = await sut.GetGoalsAsync(group.Id, match.Id, CancellationToken.None);

        goals.Should().HaveCount(3);

        goals[0].TimeSeconds.Should().Be(10);
        goals[1].TimeSeconds.Should().Be(20);
        goals[2].TimeSeconds.Should().BeNull();

        goals.All(g => !string.IsNullOrWhiteSpace(g.ScorerName)).Should().BeTrue();
        goals.Select(g => g.ScorerPlayerId).Should().NotContain(Guid.Empty);
    }

    [Fact]
    public async Task RewindOneStepAsync_WhenMatchMaking_ShouldRewindToAcceptation()
    {
        await using var db = DbContextFactory.Create(nameof(RewindOneStepAsync_WhenMatchMaking_ShouldRewindToAcceptation));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        var (match, _) = await SeedMatchAsync(
            db,
            group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.MatchMaking,
            acceptAllInvites: true,
            defineTeamsIfPossible: true);

        // sanity
        var before = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        before.Status.Should().Be(MatchStatus.MatchMaking);

        // limpa o tracker para evitar conflito de instâncias ao chamar _context.Update no serviço
        db.ChangeTracker.Clear();

        await sut.RewindOneStepAsync(group.Id, match.Id, CancellationToken.None);

        var after = await db.Matches
            .AsNoTracking()
            .FirstAsync(m => m.Id == match.Id);

        after.Status.Should().Be(MatchStatus.Acceptation);
    }

    // =========================
    // ADD GUEST TO MATCH
    // =========================

    [Fact]
    public async Task AddGuestToMatch_HappyPath_CreatesGuestPlayerAndAddsToMatch()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestToMatch_HappyPath_CreatesGuestPlayerAndAddsToMatch));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        var (match, _) = await SeedMatchAsync(db, group.Id, playersCount: 0, targetStatus: MatchStatus.Acceptation);
        db.ChangeTracker.Clear();

        await sut.AddGuestToMatchAsync(group.Id, match.Id, new AddGuestToMatchDto("Zé Convidado", false), CancellationToken.None);

        // Player guest criado corretamente no grupo
        var guest = await db.Players.FirstOrDefaultAsync(p => p.GroupId == group.Id && p.Name == "Zé Convidado");
        guest.Should().NotBeNull();
        guest!.IsGuest.Should().BeTrue();
        guest.UserId.Should().BeNull();
        guest.IsGoalkeeper.Should().BeFalse();

        // MatchPlayer adicionado na partida com Team = 0 (pendente)
        var updatedMatch = await db.Matches.Include(m => m.Players).FirstAsync(m => m.Id == match.Id);
        updatedMatch.Players.Should().HaveCount(1);
        updatedMatch.Players.Single().PlayerId.Should().Be(guest.Id);
        updatedMatch.Players.Single().Team.Should().Be(0);
    }

    [Fact]
    public async Task AddGuestToMatch_AsGoalkeeper_SetsGoalkeeperFlag()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestToMatch_AsGoalkeeper_SetsGoalkeeperFlag));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        var (match, _) = await SeedMatchAsync(db, group.Id, playersCount: 0, targetStatus: MatchStatus.Acceptation);
        db.ChangeTracker.Clear();

        await sut.AddGuestToMatchAsync(group.Id, match.Id, new AddGuestToMatchDto("Goleirão", true), CancellationToken.None);

        var guest = await db.Players.FirstAsync(p => p.GroupId == group.Id && p.Name == "Goleirão");
        guest.IsGoalkeeper.Should().BeTrue();
    }

    [Fact]
    public async Task AddGuestToMatch_WithExistingPlayers_AddsOnlyNewGuest()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestToMatch_WithExistingPlayers_AddsOnlyNewGuest));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        // Partida com 2 jogadores já presentes
        var (match, _) = await SeedMatchAsync(db, group.Id, playersCount: 2, targetStatus: MatchStatus.Acceptation);
        db.ChangeTracker.Clear();

        await sut.AddGuestToMatchAsync(group.Id, match.Id, new AddGuestToMatchDto("Novo Convidado", false), CancellationToken.None);

        var updatedMatch = await db.Matches.Include(m => m.Players).FirstAsync(m => m.Id == match.Id);
        updatedMatch.Players.Should().HaveCount(3);

        var guest = await db.Players.FirstOrDefaultAsync(p => p.GroupId == group.Id && p.Name == "Novo Convidado" && p.IsGuest);
        guest.Should().NotBeNull();
        updatedMatch.Players.Should().Contain(mp => mp.PlayerId == guest!.Id);
    }

    [Fact]
    public async Task AddGuestToMatch_WhenMatchNotFound_ShouldThrow()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestToMatch_WhenMatchNotFound_ShouldThrow));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        var act = async () => await sut.AddGuestToMatchAsync(
            group.Id, Guid.NewGuid(),
            new AddGuestToMatchDto("Fulano", false),
            CancellationToken.None);

        await act.Should().ThrowAsync<ApplicationException>().WithMessage("Match not found.");
    }

    [Fact]
    public async Task AddGuestToMatch_WhenMatchNotInAcceptation_ShouldThrow()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestToMatch_WhenMatchNotInAcceptation_ShouldThrow));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        // Match em Created (status 0), não em Acceptation
        var match = new MatchEntity(group.Id, DateTime.UtcNow, "Arena");
        db.Matches.Add(match);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var act = async () => await sut.AddGuestToMatchAsync(
            group.Id, match.Id,
            new AddGuestToMatchDto("Fulano", false),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Acceptation*");
    }

    [Fact]
    public async Task AddGuestToMatch_WhenMatchInMatchMaking_ShouldThrow()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestToMatch_WhenMatchInMatchMaking_ShouldThrow));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        var (match, _) = await SeedMatchAsync(
            db, group.Id, playersCount: 2,
            targetStatus: MatchStatus.MatchMaking,
            acceptAllInvites: true, defineTeamsIfPossible: true);
        db.ChangeTracker.Clear();

        var act = async () => await sut.AddGuestToMatchAsync(
            group.Id, match.Id,
            new AddGuestToMatchDto("Fulano", false),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Acceptation*");
    }

    // =========================
    // REWIND — clear + resync
    // =========================

    [Fact]
    public async Task RewindOneStepAsync_WhenMatchMaking_ShouldClearAllMatchPlayersAndResync()
    {
        await using var db = DbContextFactory.Create(nameof(RewindOneStepAsync_WhenMatchMaking_ShouldClearAllMatchPlayersAndResync));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        // Cria partida em MatchMaking com 2 jogadores com times atribuídos
        var (match, players) = await SeedMatchAsync(
            db, group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.MatchMaking,
            acceptAllInvites: true,
            defineTeamsIfPossible: true);

        // Sanity: jogadores têm times antes do rewind
        var before = await db.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .FirstAsync(m => m.Id == match.Id);

        before.Players.Should().HaveCount(2);
        before.Players.All(p => p.Team != 0).Should().BeTrue("times devem estar atribuídos antes do rewind");

        db.ChangeTracker.Clear();

        await sut.RewindOneStepAsync(group.Id, match.Id, CancellationToken.None);

        var after = await db.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .FirstAsync(m => m.Id == match.Id);

        after.Status.Should().Be(MatchStatus.Acceptation);

        // Todos os MatchPlayers foram re-criados pelo resync com estado limpo
        after.Players.Should().HaveCount(players.Count,
            "resync deve recriar um MatchPlayer por jogador do grupo");

        after.Players.All(p => p.Team == 0).Should().BeTrue(
            "times devem ser zerados após rewind + resync");

        after.Players.All(p => p.InviteResponse == InviteResponse.None).Should().BeTrue(
            "InviteResponse deve voltar para None após resync");
    }

    [Fact]
    public async Task RewindOneStepAsync_WhenMatchMaking_ShouldIncludeNewGroupPlayerAddedAfterMatchStarted()
    {
        await using var db = DbContextFactory.Create(nameof(RewindOneStepAsync_WhenMatchMaking_ShouldIncludeNewGroupPlayerAddedAfterMatchStarted));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        // Cria partida em MatchMaking com 2 jogadores originais
        var (match, _) = await SeedMatchAsync(
            db, group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.MatchMaking,
            acceptAllInvites: true,
            defineTeamsIfPossible: true);

        // Adiciona um 3º jogador ao grupo DEPOIS que a partida já estava em MatchMaking
        var newPlayer = new PlayerEntity("P_novo", Guid.NewGuid(), group.Id, 0m, false, status: Status.Active);
        db.Players.Add(newPlayer);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        await sut.RewindOneStepAsync(group.Id, match.Id, CancellationToken.None);

        var after = await db.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .FirstAsync(m => m.Id == match.Id);

        after.Status.Should().Be(MatchStatus.Acceptation);

        // Resync deve ter incluído o novo jogador do grupo
        after.Players.Should().HaveCount(3,
            "o novo jogador do grupo deve ser incluído pelo resync após rewind");
    }

    [Fact]
    public async Task RewindOneStepAsync_WhenStarted_ShouldPreserveTeamAssignments()
    {
        await using var db = DbContextFactory.Create(nameof(RewindOneStepAsync_WhenStarted_ShouldPreserveTeamAssignments));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);

        var (match, players) = await SeedMatchAsync(
            db, group.Id,
            playersCount: 2,
            targetStatus: MatchStatus.Started,
            acceptAllInvites: true,
            defineTeamsIfPossible: true);

        db.ChangeTracker.Clear();

        await sut.RewindOneStepAsync(group.Id, match.Id, CancellationToken.None);

        var after = await db.Matches
            .AsNoTracking()
            .Include(m => m.Players)
            .FirstAsync(m => m.Id == match.Id);

        after.Status.Should().Be(MatchStatus.MatchMaking);

        // Rewind de Started → MatchMaking não deve tocar os jogadores
        after.Players.Should().HaveCount(players.Count);
        after.Players.Any(p => p.Team != 0).Should().BeTrue(
            "atribuições de time devem ser preservadas ao voltar de Started para MatchMaking");
    }
}