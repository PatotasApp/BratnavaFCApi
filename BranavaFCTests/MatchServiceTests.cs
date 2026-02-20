using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    // =========================
    // ✅ Repo mock (realmente lê do MESMO db do SUT)
    // =========================
    private static Mock<IRepositoryBase<MatchEntity>> BuildRepoMock(AppDbContext db)
    {
        var repo = new Mock<IRepositoryBase<MatchEntity>>(MockBehavior.Strict);

        repo.Setup(r => r.Add(It.IsAny<MatchEntity>()))
            .Callback<MatchEntity>(m => db.Matches.Add(m));

        repo.Setup(r => r.Remove(It.IsAny<MatchEntity>()))
            .Callback<MatchEntity>(m =>
            {
                // se vier detached, anexa antes de remover
                if (db.Entry(m).State == EntityState.Detached)
                    db.Matches.Attach(m);

                db.Matches.Remove(m);
            });

        // ✅ IMPORTANTÍSSIMO:
        // Se o service carregou o Match via db (tracked), chamar Update() aqui
        // pode sujar o grafo inteiro e causar DbUpdateConcurrencyException.
        // Então fazemos NO-OP.
        repo.Setup(r => r.Update(It.IsAny<MatchEntity>()))
            .Callback<MatchEntity>(_ => { });

        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct => db.SaveChangesAsync(ct));

        // ✅ Agora sim: o repo devolve a partida REAL do banco, com Includes.
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, ct) =>
                db.Matches
                    .Include(m => m.Players)
                    .Include(m => m.Goals)
                    .Include(m => m.Votes)
                    .FirstOrDefaultAsync(m => m.Id == id, ct));

        repo.Setup(r => r.GetByIdIncludingInactiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, ct) =>
                db.Matches
                    .Include(m => m.Players)
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
    private static async Task<GroupEntity> SeedGroupAsync(AppDbContext db)
    {
        var group = new GroupEntity("Bratnava FC", null);
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task<List<PlayerEntity>> SeedPlayersAsync(AppDbContext db, Guid groupId, int count = 2)
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

    private static async Task<MatchEntity> SeedMatchWithPlayersAsync(
        AppDbContext db,
        Guid groupId,
        IEnumerable<PlayerEntity> players,
        MatchStatus status,
        bool acceptedInvites = true,
        bool teamsDefined = true,
        bool withScore = false,
        bool withVotes = false,
        bool finalized = false)
    {
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Boca Jrs");
        db.Matches.Add(match);

        foreach (var p in players)
        {
            var mp = new MatchPlayerEntity(p.Id);
            match.AddPlayer(mp, p);
            if (acceptedInvites)
                mp.InviteResponse = InviteResponse.Accepted;
        }

        if (teamsDefined)
        {
            var ids = match.Players.Select(x => x.PlayerId).ToList();
            match.AssignTeams(new List<Guid> { ids[0] }, new List<Guid> { ids[1] });
        }

        if (status is MatchStatus.Started or MatchStatus.Ended or MatchStatus.Finalized)
            match.Start();

        if (status is MatchStatus.Ended or MatchStatus.Finalized)
            match.End();

        if (withScore)
        {
            if (match.Status != MatchStatus.Ended) match.End();
            match.SetScore(1, 0);
        }

        if (withVotes)
        {
            if (match.Status != MatchStatus.Ended) match.End();
            var voter = match.Players[0];
            var voted = match.Players[1];
            var vote = match.CreateVote(voter.Id, voted.Id);
            db.Votes.Add(vote);
        }

        if (finalized)
        {
            if (!match.TeamAGoals.HasValue || !match.TeamBGoals.HasValue)
            {
                if (match.Status != MatchStatus.Ended) match.End();
                match.SetScore(1, 0);
            }
            match.FinalizeByVotes();
        }

        await db.SaveChangesAsync();
        return match;
    }

    private static async Task<(MatchEntity match, List<PlayerEntity> players)> SeedMatchWith3PlayersForAssistAsync(
        AppDbContext db,
        Guid groupId,
        MatchStatus status)
    {
        var players = await SeedPlayersAsync(db, groupId, 3);

        var match = new MatchEntity(groupId, DateTime.UtcNow, "Boca Jrs");
        db.Matches.Add(match);

        foreach (var p in players)
        {
            var mp = new MatchPlayerEntity(p.Id);
            match.AddPlayer(mp, p);
            mp.InviteResponse = InviteResponse.Accepted;
        }

        match.AssignTeams(
            teamAPlayerIds: new[] { players[0].Id, players[1].Id },
            teamBPlayerIds: new[] { players[2].Id });

        if (status is MatchStatus.Started or MatchStatus.Ended or MatchStatus.Finalized)
            match.Start();

        if (status is MatchStatus.Ended or MatchStatus.Finalized)
            match.End();

        if (status == MatchStatus.Finalized)
        {
            match.SetScore(0, 0);
            match.FinalizeByVotes();
        }

        await db.SaveChangesAsync();
        return (match, players);
    }

    private static async Task<(MatchEntity match, List<PlayerEntity> players)> SeedMatchWith4PlayersForScoreAsync(
        AppDbContext db,
        Guid groupId,
        MatchStatus status)
    {
        var players = await SeedPlayersAsync(db, groupId, 4);

        var match = new MatchEntity(groupId, DateTime.UtcNow, "Boca Jrs");
        db.Matches.Add(match);

        foreach (var p in players)
        {
            var mp = new MatchPlayerEntity(p.Id);
            match.AddPlayer(mp, p);
            mp.InviteResponse = InviteResponse.Accepted;
        }

        match.AssignTeams(
            teamAPlayerIds: new[] { players[0].Id, players[1].Id },
            teamBPlayerIds: new[] { players[2].Id, players[3].Id });

        if (status is MatchStatus.Started or MatchStatus.Ended or MatchStatus.Finalized)
            match.Start();

        if (status is MatchStatus.Ended or MatchStatus.Finalized)
            match.End();

        await db.SaveChangesAsync();
        return (match, players);
    }

    [Fact]
    public void AddGoal_WhenValidWithAssistSameTeam_ShouldAddGoal_AndRecalculateScore()
    {
        var (match, mpA1, mpA2, _) = CreateMatchWithThreePlayers_TwoInA_OneInB();

        match.AddGoalByMatchPlayer(
            scorerMatchPlayerId: mpA1.Id,
            assistMatchPlayerId: mpA2.Id,
            timeSeconds: 12 * 60 + 34);

        Assert.Single(match.Goals);

        var g = match.Goals[0];

        Assert.Equal(mpA1.Id, g.ScorerMatchPlayerId);
        Assert.Equal(mpA2.Id, g.AssistMatchPlayerId);
        Assert.Equal(12 * 60 + 34, g.TimeSeconds);

        Assert.Equal(1, match.TeamAGoals);
        Assert.Equal(0, match.TeamBGoals);
    }



    [Fact]
    public async Task RemoveGoalAsync_WhenExists_ShouldRemove_AndUpdateScore()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveGoalAsync_WhenExists_ShouldRemove_AndUpdateScore));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchWith3PlayersForAssistAsync(db, group.Id, MatchStatus.Started);

        // cria 2 gols
        var tracked = await db.Matches
            .Include(m => m.Goals)
            .Include(m => m.Players)
            .FirstAsync(m => m.Id == match.Id);

        var scorerMpId = tracked.Players
            .First(mp => mp.PlayerId == players[0].Id)
            .Id;

        tracked.AddGoalByMatchPlayer(scorerMpId, null, 10);
        tracked.AddGoalByMatchPlayer(scorerMpId, null, 20);
        await db.SaveChangesAsync();

        var goalId = tracked.Goals[0].Id;

        db.ChangeTracker.Clear();

        await sut.RemoveGoalAsync(group.Id, match.Id, goalId, CancellationToken.None);

        var reloaded = await db.Matches
            .AsNoTracking()
            .Include(m => m.Goals)
            .FirstAsync(m => m.Id == match.Id);

        reloaded.Goals.Should().HaveCount(1);
        reloaded.TeamAGoals.Should().Be(1);
        reloaded.TeamBGoals.Should().Be(0);
    }

    [Fact]
    public async Task Score_ShouldBeCalculatedFromGoals_Example9x8_Service()
    {
        await using var db = DbContextFactory.Create(nameof(Score_ShouldBeCalculatedFromGoals_Example9x8_Service));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchWith4PlayersForScoreAsync(db, group.Id, MatchStatus.Started);

        db.ChangeTracker.Clear();

        var a1 = players[0];
        var a2 = players[1];
        var b1 = players[2];
        var b2 = players[3];

        for (int i = 0; i < 9; i++)
        {
            var scorer = (i % 2 == 0) ? a1 : a2;
            await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(scorer.Id, null, null), CancellationToken.None);
        }

        for (int i = 0; i < 8; i++)
        {
            var scorer = (i % 2 == 0) ? b1 : b2;
            await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(scorer.Id, null, null), CancellationToken.None);
        }

        var reloaded = await db.Matches
            .AsNoTracking()
            .FirstAsync(m => m.Id == match.Id);

        reloaded.TeamAGoals.Should().Be(9);
        reloaded.TeamBGoals.Should().Be(8);
    }

    private static (
    MatchEntity match,
    MatchPlayerEntity mpA1,
    MatchPlayerEntity mpA2,
    MatchPlayerEntity mpB1)
CreateMatchWithThreePlayers_TwoInA_OneInB(
    bool start = true,
    bool end = false)
    {
        var groupId = Guid.NewGuid();
        var match = new MatchEntity(groupId, DateTime.UtcNow, "Boca Jrs");

        var playerA1 = new PlayerEntity("A1", Guid.NewGuid(), groupId, 0, false, Status.Active);
        var playerA2 = new PlayerEntity("A2", Guid.NewGuid(), groupId, 0, false, Status.Active);
        var playerB1 = new PlayerEntity("B1", Guid.NewGuid(), groupId, 0, false, Status.Active);

        var mpA1 = new MatchPlayerEntity(playerA1.Id);
        var mpA2 = new MatchPlayerEntity(playerA2.Id);
        var mpB1 = new MatchPlayerEntity(playerB1.Id);

        match.AddPlayer(mpA1, playerA1);
        match.AddPlayer(mpA2, playerA2);
        match.AddPlayer(mpB1, playerB1);

        mpA1.InviteResponse = InviteResponse.Accepted;
        mpA2.InviteResponse = InviteResponse.Accepted;
        mpB1.InviteResponse = InviteResponse.Accepted;

        match.AssignTeams(
            teamAPlayerIds: new[] { playerA1.Id, playerA2.Id },
            teamBPlayerIds: new[] { playerB1.Id });

        if (start) match.Start();
        if (end) match.End();

        return (match, mpA1, mpA2, mpB1);
    }

}
