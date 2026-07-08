using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>
/// Testes adicionais de MatchService cobrindo métodos e branches não exercitados
/// pelos demais arquivos de teste (MatchServiceTests, MatchService_NewFeaturesTests, etc).
/// </summary>
public sealed class MatchServiceCoverageTests
{
    // =========================
    // SUT / Seeds
    // =========================

    private static MatchService CreateSut(
        AppDbContext db,
        IReplayUrlService? replayUrls = null,
        INotificationScheduler? scheduler = null)
    {
        var repo = new RepositoryBase<MatchEntity>(db);
        return new MatchService(
            db,
            repo,
            Mock.Of<IPushService>(),
            replayUrls ?? Mock.Of<IReplayUrlService>(),
            Mock.Of<IBetService>(),
            scheduler ?? Mock.Of<INotificationScheduler>());
    }

    private static async Task<GroupEntity> SeedGroupAsync(AppDbContext db, string name = "Bratnava FC")
    {
        var group = new GroupEntity(name, null, Guid.NewGuid());
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

        foreach (var p in players)
        {
            var mp = new MatchPlayerEntity(p.Id);
            match.AddPlayer(mp, p);
        }

        await db.SaveChangesAsync();

        if (targetStatus >= MatchStatus.Acceptation)
        {
            match.OpenAcceptation();

            if (acceptAllInvites)
            {
                foreach (var p in players)
                    match.AcceptInvite(p.Id);
            }
        }

        if (targetStatus >= MatchStatus.MatchMaking)
        {
            match.GoToMatchMaking();

            if (defineTeamsIfPossible && players.Count >= 2)
            {
                var ids = players.Select(p => p.Id).ToList();
                var split = Math.Max(1, ids.Count / 2);
                match.AssignTeams(ids.Take(split).ToList(), ids.Skip(split).ToList());
            }
        }

        if (targetStatus >= MatchStatus.Started)
            match.Start();

        if (targetStatus >= MatchStatus.Ended)
            match.End();

        if (targetStatus >= MatchStatus.PostGame)
            match.GoToPostGame();

        if (setScoreInPostGame)
        {
            var (a, b) = score ?? (1, 0);
            match.SetScore(a, b);
        }

        if (targetStatus >= MatchStatus.Finalized)
        {
            if (!match.TeamAGoals.HasValue || !match.TeamBGoals.HasValue)
                match.SetScore(1, 0);

            match.FinalizeByVotes();
        }

        await db.SaveChangesAsync();
        return (match, players);
    }

    private static ReplayClipEntity MakeClip(
        Guid groupId, Guid matchId,
        string objectKey = "clip.mp4",
        MatchEventType eventType = MatchEventType.Gol,
        DateTimeOffset? recordedAt = null) =>
        new(groupId, matchId, "goal-replays", objectKey, "video/mp4", "etag",
            recordedAt ?? DateTimeOffset.UtcNow, eventType);

    private static async Task<UserEntity> SeedUserAsync(AppDbContext db, string userName)
    {
        var user = new UserEntity(userName, "First", "Last", $"{userName}@x.com", "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // =========================
    // GET ALL / GET BY ID / DETAILS
    // =========================

    [Fact]
    public async Task GetAllAsync_WhenGroupNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WhenGroupNotFound_ShouldFail));
        var sut = CreateSut(db);

        var result = await sut.GetAllAsync(Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetAllAsync_WhenEmptyGroupId_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WhenEmptyGroupId_ShouldFail));
        var sut = CreateSut(db);

        var result = await sut.GetAllAsync(Guid.Empty, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("GroupId é obrigatório.");
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnMatchesOrderedByPlayedAtDesc()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_ShouldReturnMatchesOrderedByPlayedAtDesc));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (older, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false, playedAtUtc: DateTime.UtcNow.AddDays(-2));
        var (newer, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false, playedAtUtc: DateTime.UtcNow.AddDays(-1));

        var result = await sut.GetAllAsync(group.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data![0].MatchId.Should().Be(newer.Id);
        result.Data![1].MatchId.Should().Be(older.Id);
        result.Data![0].GroupName.Should().Be("Bratnava FC");
    }

    [Fact]
    public async Task GetByIdAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.GetByIdAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Partida não encontrada.");
    }

    [Fact]
    public async Task GetByIdAsync_WhenExists_ShouldReturnMatchWithPlayers()
    {
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_WhenExists_ShouldReturnMatchWithPlayers));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 3, MatchStatus.Acceptation, acceptAllInvites: false);

        var result = await sut.GetByIdAsync(group.Id, match.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(match.Id);
        result.Data!.Players.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetDetailsAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GetDetailsAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var result = await sut.GetDetailsAsync(Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetDetailsAsync_ShouldMapTeamsGoalsAndVotes()
    {
        await using var db = DbContextFactory.Create(nameof(GetDetailsAsync_ShouldMapTeamsGoalsAndVotes));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 4, MatchStatus.PostGame, setScoreInPostGame: true, score: (2, 1));

        await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[0].Id, players[1].Id, "01:00"), CancellationToken.None);

        var mp0 = match.Players.First(p => p.PlayerId == players[0].Id);
        var mp1 = match.Players.First(p => p.PlayerId == players[1].Id);
        await sut.VoteAsync(group.Id, match.Id, mp0.Id, mp1.Id, CancellationToken.None);

        var result = await sut.GetDetailsAsync(match.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dto = result.Data!;
        dto.MatchId.Should().Be(match.Id);
        dto.GroupName.Should().Be("Bratnava FC");
        dto.TeamAPlayers.Should().HaveCount(2);
        dto.TeamBPlayers.Should().HaveCount(2);
        dto.Goals.Should().HaveCount(1);
        dto.Goals[0].AssistName.Should().NotBeNullOrEmpty();
        dto.Votes.Should().HaveCount(1);
        dto.VoteCounts.Should().ContainSingle(v => v.VotedForMatchPlayerId == mp1.Id && v.Count == 1);
    }

    // =========================
    // UPDATE / DELETE
    // =========================

    [Fact]
    public async Task UpdateAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.UpdateAsync(group.Id, Guid.NewGuid(), new UpdateMatchDto(null, DateTime.UtcNow, "X"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task UpdateAsync_WhenValid_ShouldUpdateDateAndPlace()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenValid_ShouldUpdateDateAndPlace));
        var scheduler = new Mock<INotificationScheduler>();
        var sut = CreateSut(db, scheduler: scheduler.Object);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false);

        var newDate = DateTime.UtcNow.AddDays(7);
        var result = await sut.UpdateAsync(group.Id, match.Id, new UpdateMatchDto(match.Id, newDate, "Maracanã"), CancellationToken.None);

        result.Success.Should().BeTrue();

        var reloaded = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        reloaded.PlayedAt.Should().Be(newDate);
        reloaded.PlaceName.Should().Be("Maracanã");

        scheduler.Verify(s => s.RescheduleMatchRemindersAsync(match.Id, group.Id, newDate, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenMatchNotFound_ShouldReturnOk()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WhenMatchNotFound_ShouldReturnOk));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.DeleteAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_WhenValid_ShouldRemoveMatch_AndCancelJobs()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WhenValid_ShouldRemoveMatch_AndCancelJobs));
        var scheduler = new Mock<INotificationScheduler>();
        var sut = CreateSut(db, scheduler: scheduler.Object);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false);

        var result = await sut.DeleteAsync(group.Id, match.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        (await db.Matches.AsNoTracking().AnyAsync(m => m.Id == match.Id)).Should().BeFalse();
        scheduler.Verify(s => s.CancelMatchRemindersAsync(match.Id, It.IsAny<CancellationToken>()), Times.Once);
        scheduler.Verify(s => s.CancelMvpAutoFinalizeAsync(match.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenFinalized_ShouldThrow()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WhenFinalized_ShouldThrow));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Finalized, setScoreInPostGame: true);

        var act = async () => await sut.DeleteAsync(group.Id, match.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Partida ja Finalizada. Nao e possivel excluir.");
    }

    // =========================
    // MY INVITE (JWT userId)
    // =========================

    [Fact]
    public async Task AcceptMyInviteAsync_WhenUserNotInGroup_ShouldFailNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(AcceptMyInviteAsync_WhenUserNotInGroup_ShouldFailNotFound));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false);

        var result = await sut.AcceptMyInviteAsync(group.Id, match.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Jogador não encontrado no grupo.");
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task AcceptMyInviteAsync_WhenValid_ShouldAcceptByUserId()
    {
        await using var db = DbContextFactory.Create(nameof(AcceptMyInviteAsync_WhenValid_ShouldAcceptByUserId));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false);

        var result = await sut.AcceptMyInviteAsync(group.Id, match.Id, players[0].UserId!.Value, CancellationToken.None);

        result.Success.Should().BeTrue();
        var mp = await db.MatchPlayers.AsNoTracking()
            .FirstAsync(x => x.MatchId == match.Id && x.PlayerId == players[0].Id);
        mp.InviteResponse.Should().Be(InviteResponse.Accepted);
    }

    [Fact]
    public async Task AcceptMyInviteAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(AcceptMyInviteAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 1);

        var result = await sut.AcceptMyInviteAsync(group.Id, Guid.NewGuid(), players[0].UserId!.Value, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task RejectMyInviteAsync_WhenValid_ShouldRejectByUserId()
    {
        await using var db = DbContextFactory.Create(nameof(RejectMyInviteAsync_WhenValid_ShouldRejectByUserId));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false);

        var result = await sut.RejectMyInviteAsync(group.Id, match.Id, players[1].UserId!.Value, CancellationToken.None);

        result.Success.Should().BeTrue();
        var mp = await db.MatchPlayers.AsNoTracking()
            .FirstAsync(x => x.MatchId == match.Id && x.PlayerId == players[1].Id);
        mp.InviteResponse.Should().Be(InviteResponse.Rejected);
    }

    [Fact]
    public async Task RejectMyInviteAsync_WhenUserNotInGroup_ShouldFailNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(RejectMyInviteAsync_WhenUserNotInGroup_ShouldFailNotFound));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false);

        var result = await sut.RejectMyInviteAsync(group.Id, match.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // =========================
    // START / END / STATE — error paths
    // =========================

    [Fact]
    public async Task StartMatchAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(StartMatchAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.StartMatchAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task EndMatchAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(EndMatchAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.EndMatchAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task EndMatchAsync_WhenAutoFinalizeConfigured_ShouldScheduleMvpAutoFinalize()
    {
        await using var db = DbContextFactory.Create(nameof(EndMatchAsync_WhenAutoFinalizeConfigured_ShouldScheduleMvpAutoFinalize));
        var scheduler = new Mock<INotificationScheduler>();
        var sut = CreateSut(db, scheduler: scheduler.Object);

        var group = await SeedGroupAsync(db);
        var settings = new GroupSettingsEntity(group.Id, 2, 10, null, null, null);
        settings.SetAutoFinalizeMvpHours(6);
        db.GroupSettings.Add(settings);
        await db.SaveChangesAsync();

        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Started);

        var result = await sut.EndMatchAsync(group.Id, match.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        scheduler.Verify(s => s.ScheduleMvpAutoFinalizeAsync(match.Id, group.Id, 6, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GoToMatchMakingAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GoToMatchMakingAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.GoToMatchMakingAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GoToPostGameAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GoToPostGameAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.GoToPostGameAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task RewindOneStepAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(RewindOneStepAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.RewindOneStepAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // =========================
    // VOTE / MVP
    // =========================

    [Fact]
    public async Task VoteAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(VoteAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.VoteAsync(group.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetMvpAsync_WhenNoVotes_ShouldFailNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(GetMvpAsync_WhenNoVotes_ShouldFailNotFound));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.PostGame);

        var result = await sut.GetMvpAsync(group.Id, match.Id, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("MVP ainda não computado.");
    }

    [Fact]
    public async Task GetMvpAsync_WhenVotesExist_ShouldReturnMostVoted()
    {
        await using var db = DbContextFactory.Create(nameof(GetMvpAsync_WhenVotesExist_ShouldReturnMostVoted));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 3, MatchStatus.PostGame);

        var mp0 = match.Players.First(p => p.PlayerId == players[0].Id);
        var mp1 = match.Players.First(p => p.PlayerId == players[1].Id);
        var mp2 = match.Players.First(p => p.PlayerId == players[2].Id);

        await sut.VoteAsync(group.Id, match.Id, mp0.Id, mp1.Id, CancellationToken.None);
        await sut.VoteAsync(group.Id, match.Id, mp2.Id, mp1.Id, CancellationToken.None);

        var result = await sut.GetMvpAsync(group.Id, match.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(mp1.Id);
    }

    [Fact]
    public async Task GetMvpAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GetMvpAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.GetMvpAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task ReapplyMvpTieRuleAsync_WhenWrongStatus_ShouldFailBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(ReapplyMvpTieRuleAsync_WhenWrongStatus_ShouldFailBadRequest));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.MatchMaking);

        var result = await sut.ReapplyMvpTieRuleAsync(group.Id, match.Id, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task ReapplyMvpTieRuleAsync_WhenPostGame_ShouldRecomputeMvp()
    {
        await using var db = DbContextFactory.Create(nameof(ReapplyMvpTieRuleAsync_WhenPostGame_ShouldRecomputeMvp));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 3, MatchStatus.PostGame);

        var mp0 = match.Players.First(p => p.PlayerId == players[0].Id);
        var mp1 = match.Players.First(p => p.PlayerId == players[1].Id);
        var mp2 = match.Players.First(p => p.PlayerId == players[2].Id);

        await sut.VoteAsync(group.Id, match.Id, mp0.Id, mp1.Id, CancellationToken.None);
        await sut.VoteAsync(group.Id, match.Id, mp2.Id, mp1.Id, CancellationToken.None);

        var result = await sut.ReapplyMvpTieRuleAsync(group.Id, match.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.MatchPlayers.AsNoTracking().FirstAsync(x => x.Id == mp1.Id);
        reloaded.IsMvp.Should().BeTrue();
    }

    [Fact]
    public async Task ReapplyMvpTieRuleAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(ReapplyMvpTieRuleAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.ReapplyMvpTieRuleAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // =========================
    // SCORE / COLORS / FINALIZE — error paths
    // =========================

    [Fact]
    public async Task SetScoreAsync_WhenPostGame_ShouldPersistScore()
    {
        await using var db = DbContextFactory.Create(nameof(SetScoreAsync_WhenPostGame_ShouldPersistScore));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.PostGame);

        var result = await sut.SetScoreAsync(group.Id, match.Id, 3, 2, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        reloaded.TeamAGoals.Should().Be(3);
        reloaded.TeamBGoals.Should().Be(2);
    }

    [Fact]
    public async Task SetScoreAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(SetScoreAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.SetScoreAsync(group.Id, Guid.NewGuid(), 1, 0, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task SetTeamColorsAsync_WhenRandomize_ShouldPickTwoDistinctColors()
    {
        await using var db = DbContextFactory.Create(nameof(SetTeamColorsAsync_WhenRandomize_ShouldPickTwoDistinctColors));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        db.TeamColors.AddRange(
            new TeamColorEntity(group.Id, "Azul", "#0000FF"),
            new TeamColorEntity(group.Id, "Verde", "#00FF00"),
            new TeamColorEntity(group.Id, "Preto", "#000000"));
        await db.SaveChangesAsync();

        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.MatchMaking);

        var result = await sut.SetTeamColorsAsync(group.Id, match.Id, null, null, randomize: true, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        reloaded.TeamAColorId.Should().NotBeNull();
        reloaded.TeamBColorId.Should().NotBeNull();
        reloaded.TeamAColorId.Should().NotBe(reloaded.TeamBColorId!.Value);
    }

    [Fact]
    public async Task SetTeamColorsAsync_WhenTeamAColorNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(SetTeamColorsAsync_WhenTeamAColorNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.MatchMaking);

        var result = await sut.SetTeamColorsAsync(group.Id, match.Id, Guid.NewGuid(), null, randomize: false, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Cor do time A nao encontrada.");
    }

    [Fact]
    public async Task SetTeamColorsAsync_WhenTeamBColorNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(SetTeamColorsAsync_WhenTeamBColorNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var colorA = new TeamColorEntity(group.Id, "Azul", "#0000FF");
        db.TeamColors.Add(colorA);
        await db.SaveChangesAsync();

        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.MatchMaking);

        var result = await sut.SetTeamColorsAsync(group.Id, match.Id, colorA.Id, Guid.NewGuid(), randomize: false, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Cor do time B nao encontrada.");
    }

    [Fact]
    public async Task SetTeamColorsAsync_WhenValidColors_ShouldPersist()
    {
        await using var db = DbContextFactory.Create(nameof(SetTeamColorsAsync_WhenValidColors_ShouldPersist));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var colorA = new TeamColorEntity(group.Id, "Azul", "#0000FF");
        var colorB = new TeamColorEntity(group.Id, "Verde", "#00FF00");
        db.TeamColors.AddRange(colorA, colorB);
        await db.SaveChangesAsync();

        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.MatchMaking);

        var result = await sut.SetTeamColorsAsync(group.Id, match.Id, colorA.Id, colorB.Id, randomize: false, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        reloaded.TeamAColorId.Should().Be(colorA.Id);
        reloaded.TeamBColorId.Should().Be(colorB.Id);
    }

    [Fact]
    public async Task SetTeamColorsAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(SetTeamColorsAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.SetTeamColorsAsync(group.Id, Guid.NewGuid(), null, null, randomize: true, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task FinalizeMatchAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(FinalizeMatchAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.FinalizeMatchAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // =========================
    // ASSIGN / SWAP / ROLE — error paths
    // =========================

    [Fact]
    public async Task AssignTeamsAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(AssignTeamsAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var dto = new AssignTeamsDto
        {
            TeamAMatchPlayerIds = [Guid.NewGuid()],
            TeamBMatchPlayerIds = [Guid.NewGuid()],
        };

        var result = await sut.AssignTeamsAsync(group.Id, Guid.NewGuid(), dto, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task AssignTeamsAsync_WithEmptyMatchId_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(AssignTeamsAsync_WithEmptyMatchId_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.AssignTeamsAsync(group.Id, Guid.Empty, new AssignTeamsDto(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("MatchId é obrigatório.");
    }

    [Fact]
    public async Task SwapPlayersByPlayerIdAsync_WithEmptyPlayerId_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(SwapPlayersByPlayerIdAsync_WithEmptyPlayerId_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.MatchMaking);

        var result = await sut.SwapPlayersByPlayerIdAsync(group.Id, match.Id, Guid.Empty, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("PlayerId é obrigatório.");
    }

    [Fact]
    public async Task SwapPlayersByPlayerIdAsync_WithSamePlayer_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(SwapPlayersByPlayerIdAsync_WithSamePlayer_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.MatchMaking);

        var result = await sut.SwapPlayersByPlayerIdAsync(group.Id, match.Id, players[0].Id, players[0].Id, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Não é possível trocar o mesmo jogador.");
    }

    [Fact]
    public async Task SwapPlayersByPlayerIdAsync_WhenPlayerNotInMatch_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(SwapPlayersByPlayerIdAsync_WhenPlayerNotInMatch_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.MatchMaking);

        var result = await sut.SwapPlayersByPlayerIdAsync(group.Id, match.Id, players[0].Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Um ou ambos os jogadores não pertencem a esta partida.");
    }

    [Fact]
    public async Task SwapPlayersByPlayerIdAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(SwapPlayersByPlayerIdAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.SwapPlayersByPlayerIdAsync(group.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task SetPlayerRoleAsync_WhenValid_ShouldSetGoalkeeper()
    {
        await using var db = DbContextFactory.Create(nameof(SetPlayerRoleAsync_WhenValid_ShouldSetGoalkeeper));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false);

        var mp = match.Players.First(p => p.PlayerId == players[0].Id);

        var result = await sut.SetPlayerRoleAsync(group.Id, match.Id, mp.Id, new SetPlayerRoleDto { IsGoalkeeper = true }, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.MatchPlayers.AsNoTracking().FirstAsync(x => x.Id == mp.Id);
        reloaded.IsGoalkeeper.Should().BeTrue();
    }

    [Fact]
    public async Task SetPlayerRoleAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(SetPlayerRoleAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.SetPlayerRoleAsync(group.Id, Guid.NewGuid(), Guid.NewGuid(), new SetPlayerRoleDto(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // =========================
    // GOALS — CRUD error paths + update
    // =========================

    [Fact]
    public async Task AddGoalAsync_WhenScorerNotInMatch_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(AddGoalAsync_WhenScorerNotInMatch_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Started);

        var result = await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(Guid.NewGuid(), null, "00:10"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("O jogador do gol nao pertence a esta partida.");
    }

    [Fact]
    public async Task AddGoalAsync_WhenAssistNotInMatch_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(AddGoalAsync_WhenAssistNotInMatch_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Started);

        var result = await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[0].Id, Guid.NewGuid(), "00:10"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("O jogador da assistencia nao pertence a esta partida.");
    }

    [Fact]
    public async Task AddGoalAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(AddGoalAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.AddGoalAsync(group.Id, Guid.NewGuid(), new AddGoalRequestDto(Guid.NewGuid(), null, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task UpdateGoalAsync_WhenValid_ShouldUpdateScorerAndScore()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateGoalAsync_WhenValid_ShouldUpdateScorerAndScore));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.PostGame);

        await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[0].Id, null, "00:10"), CancellationToken.None);
        var goalId = (await db.Matches.Include(m => m.Goals).FirstAsync(m => m.Id == match.Id)).Goals[0].Id;

        var result = await sut.UpdateGoalAsync(group.Id, match.Id, goalId,
            new UpdateGoalRequestDto(players[1].Id, null, "00:30"), CancellationToken.None);

        result.Success.Should().BeTrue();

        var reloaded = await db.Matches.AsNoTracking().Include(m => m.Goals).FirstAsync(m => m.Id == match.Id);
        reloaded.Goals[0].TimeSeconds.Should().Be(30);
        // gol passou do time A (players[0]) para o time B (players[1])
        reloaded.TeamAGoals.Should().Be(0);
        reloaded.TeamBGoals.Should().Be(1);
    }

    [Fact]
    public async Task UpdateGoalAsync_WithEmptyGoalId_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateGoalAsync_WithEmptyGoalId_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.PostGame);

        var result = await sut.UpdateGoalAsync(group.Id, match.Id, Guid.Empty,
            new UpdateGoalRequestDto(players[0].Id, null, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("GoalId e obrigatorio.");
    }

    [Fact]
    public async Task UpdateGoalAsync_WhenScorerNotInMatch_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateGoalAsync_WhenScorerNotInMatch_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.PostGame);

        await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[0].Id, null, null), CancellationToken.None);
        var goalId = (await db.Matches.Include(m => m.Goals).FirstAsync(m => m.Id == match.Id)).Goals[0].Id;

        var result = await sut.UpdateGoalAsync(group.Id, match.Id, goalId,
            new UpdateGoalRequestDto(Guid.NewGuid(), null, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("O jogador do gol nao pertence a esta partida.");
    }

    [Fact]
    public async Task UpdateGoalAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateGoalAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.UpdateGoalAsync(group.Id, Guid.NewGuid(), Guid.NewGuid(),
            new UpdateGoalRequestDto(Guid.NewGuid(), null, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task RemoveGoalAsync_WithEmptyGoalId_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveGoalAsync_WithEmptyGoalId_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.PostGame);

        var result = await sut.RemoveGoalAsync(group.Id, match.Id, Guid.Empty, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("GoalId e obrigatorio.");
    }

    [Fact]
    public async Task RemoveGoalAsync_WhenGoalNotFound_ShouldReturnOk()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveGoalAsync_WhenGoalNotFound_ShouldReturnOk));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.PostGame);

        var result = await sut.RemoveGoalAsync(group.Id, match.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task AddGoalsBulkAsync_WithEmptyList_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(AddGoalsBulkAsync_WithEmptyList_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.PostGame);

        var result = await sut.AddGoalsBulkAsync(group.Id, match.Id, new AddGoalsBulkRequestDto([]), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("A lista de gols nao pode ser vazia.");
    }

    [Fact]
    public async Task AddGoalsBulkAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(AddGoalsBulkAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var goals = new List<AddGoalRequestDto> { new(Guid.NewGuid(), null, null) };
        var result = await sut.AddGoalsBulkAsync(group.Id, Guid.NewGuid(), new AddGoalsBulkRequestDto(goals), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // =========================
    // HEADER / ACCEPTATION / MATCHMAKING / POSTGAME — not found + branches
    // =========================

    [Fact]
    public async Task GetHeaderAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GetHeaderAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.GetHeaderAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetAcceptationAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GetAcceptationAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.GetAcceptationAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetAcceptationAsync_WhenPastAcceptation_PendingShouldBecomeRejected()
    {
        await using var db = DbContextFactory.Create(nameof(GetAcceptationAsync_WhenPastAcceptation_PendingShouldBecomeRejected));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        // 3 jogadores: 2 aceitam, 1 fica pendente; partida avança para MatchMaking
        var (match, players) = await SeedMatchAsync(db, group.Id, 3, MatchStatus.Acceptation, acceptAllInvites: false);
        match.AcceptInvite(players[0].Id);
        match.AcceptInvite(players[1].Id);
        match.GoToMatchMaking();
        await db.SaveChangesAsync();

        var result = await sut.GetAcceptationAsync(group.Id, match.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dto = result.Data!;
        dto.PendingPlayers.Should().BeEmpty();
        dto.RejectedPlayers.Should().ContainSingle(p => p.PlayerId == players[2].Id);
        dto.AcceptedPlayers.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAcceptationSummaryAsync_ShouldIncludeGuests()
    {
        await using var db = DbContextFactory.Create(nameof(GetAcceptationSummaryAsync_ShouldIncludeGuests));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: true);

        await sut.AddGuestToMatchAsync(group.Id, match.Id, new AddGuestToMatchDto("Convidado", false), CancellationToken.None);

        var full = await sut.GetAcceptationAsync(group.Id, match.Id, CancellationToken.None);
        var summary = await sut.GetAcceptationSummaryAsync(group.Id, match.Id, CancellationToken.None);

        full.Data!.AcceptedPlayers.Should().HaveCount(3); // 2 + convidado auto-aceito
        summary.Data!.AcceptedPlayers.Should().HaveCount(3);
        summary.Data!.AcceptedPlayers.Should().ContainSingle(p => p.IsGuest && p.PlayerName == "Convidado");
        summary.Data!.RejectedPlayers.Should().OnlyContain(p => !p.IsGuest);
        summary.Data!.PendingPlayers.Should().OnlyContain(p => !p.IsGuest);
        summary.Data!.AcceptedPlayers.Select(p => p.PlayerId)
            .Should().BeEquivalentTo(players.Select(p => p.Id).Append(summary.Data!.AcceptedPlayers.Single(p => p.IsGuest).PlayerId));
    }

    [Fact]
    public async Task GetAcceptationSummaryAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GetAcceptationSummaryAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.GetAcceptationSummaryAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetMatchMakingAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GetMatchMakingAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.GetMatchMakingAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetPostGameAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GetPostGameAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.GetPostGameAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetPostGameAsync_WhenUserVoted_ShouldReturnMyVotedForMatchPlayerId()
    {
        await using var db = DbContextFactory.Create(nameof(GetPostGameAsync_WhenUserVoted_ShouldReturnMyVotedForMatchPlayerId));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.PostGame);

        var mp0 = match.Players.First(p => p.PlayerId == players[0].Id);
        var mp1 = match.Players.First(p => p.PlayerId == players[1].Id);
        await sut.VoteAsync(group.Id, match.Id, mp0.Id, mp1.Id, CancellationToken.None);

        var result = await sut.GetPostGameAsync(group.Id, match.Id, CancellationToken.None, players[0].UserId);

        result.Success.Should().BeTrue();
        result.Data!.HasVoted.Should().BeTrue();
        result.Data!.CanVote.Should().BeFalse();
        result.Data!.MyVotedForMatchPlayerId.Should().Be(mp1.Id);
    }

    // =========================
    // UPCOMING / SYNC
    // =========================

    [Fact]
    public async Task GetUpcomingAsync_ShouldReturnNonFinalizedOrderedByPlayedAt()
    {
        await using var db = DbContextFactory.Create(nameof(GetUpcomingAsync_ShouldReturnNonFinalizedOrderedByPlayedAt));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        await SeedMatchAsync(db, group.Id, 2, MatchStatus.Finalized, setScoreInPostGame: true, playedAtUtc: DateTime.UtcNow.AddDays(-5));
        var (m2, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false, playedAtUtc: DateTime.UtcNow.AddDays(2));
        var (m1, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false, playedAtUtc: DateTime.UtcNow.AddDays(1));

        var result = await sut.GetUpcomingAsync(group.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data![0].MatchId.Should().Be(m1.Id);
        result.Data![1].MatchId.Should().Be(m2.Id);
        result.Data!.Should().OnlyContain(h => h.StatusName != nameof(MatchStatus.Finalized));
    }

    [Fact]
    public async Task GetUpcomingAsync_WhenGroupNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GetUpcomingAsync_WhenGroupNotFound_ShouldFail));
        var sut = CreateSut(db);

        var result = await sut.GetUpcomingAsync(Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task SyncPlayerIntoActiveMatchesAsync_WhenPlayerNotFound_ShouldReturnOk()
    {
        await using var db = DbContextFactory.Create(nameof(SyncPlayerIntoActiveMatchesAsync_WhenPlayerNotFound_ShouldReturnOk));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.SyncPlayerIntoActiveMatchesAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SyncPlayerIntoActiveMatchesAsync_WhenNoEligibleMatches_ShouldReturnOk()
    {
        await using var db = DbContextFactory.Create(nameof(SyncPlayerIntoActiveMatchesAsync_WhenNoEligibleMatches_ShouldReturnOk));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 1);

        var result = await sut.SyncPlayerIntoActiveMatchesAsync(group.Id, players[0].Id, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SyncPlayerIntoActiveMatchesAsync_ShouldAddPlayerToAcceptationMatch()
    {
        await using var db = DbContextFactory.Create(nameof(SyncPlayerIntoActiveMatchesAsync_ShouldAddPlayerToAcceptationMatch));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false);

        // Novo jogador criado depois da partida
        var newPlayer = new PlayerEntity("Novato", Guid.NewGuid(), group.Id, 0m, false, false, Status.Active);
        db.Players.Add(newPlayer);
        await db.SaveChangesAsync();

        var result = await sut.SyncPlayerIntoActiveMatchesAsync(group.Id, newPlayer.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        var mps = await db.MatchPlayers.AsNoTracking().Where(mp => mp.MatchId == match.Id).ToListAsync();
        mps.Should().Contain(mp => mp.PlayerId == newPlayer.Id);
    }

    [Fact]
    public async Task SyncPlayerIntoActiveMatchesAsync_WhenAlreadyInMatch_ShouldNotDuplicate()
    {
        await using var db = DbContextFactory.Create(nameof(SyncPlayerIntoActiveMatchesAsync_WhenAlreadyInMatch_ShouldNotDuplicate));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false);

        var result = await sut.SyncPlayerIntoActiveMatchesAsync(group.Id, players[0].Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        var count = await db.MatchPlayers.AsNoTracking()
            .CountAsync(mp => mp.MatchId == match.Id && mp.PlayerId == players[0].Id);
        count.Should().Be(1);
    }

    [Fact]
    public async Task SyncPlayerIntoActiveMatchesAsync_WhenPlayerHasAbsence_ShouldAutoReject()
    {
        await using var db = DbContextFactory.Create(nameof(SyncPlayerIntoActiveMatchesAsync_WhenPlayerHasAbsence_ShouldAutoReject));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var playedAt = DateTime.UtcNow.AddDays(2);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false, playedAtUtc: playedAt);

        var newPlayer = new PlayerEntity("Ausente", Guid.NewGuid(), group.Id, 0m, false, false, Status.Active);
        db.Players.Add(newPlayer);

        var matchDate = DateOnly.FromDateTime(playedAt);
        var absence = new UserAbsenceEntity(
            newPlayer.UserId!.Value,
            matchDate.AddDays(-1),
            matchDate.AddDays(1),
            AbsenceType.Travel,
            null);
        db.UserAbsences.Add(absence);
        await db.SaveChangesAsync();

        var result = await sut.SyncPlayerIntoActiveMatchesAsync(group.Id, newPlayer.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        var mp = await db.MatchPlayers.AsNoTracking()
            .FirstAsync(x => x.MatchId == match.Id && x.PlayerId == newPlayer.Id);
        mp.InviteResponse.Should().Be(InviteResponse.Rejected);
        mp.AutoRejectedByAbsenceId.Should().Be(absence.Id);
    }

    [Fact]
    public async Task SyncPlayersFromGroupAsync_ByMatchId_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(SyncPlayersFromGroupAsync_ByMatchId_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var result = await sut.SyncPlayersFromGroupAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task SyncPlayersFromGroupAsync_ByMatchId_ShouldAddNewGroupPlayers()
    {
        await using var db = DbContextFactory.Create(nameof(SyncPlayersFromGroupAsync_ByMatchId_ShouldAddNewGroupPlayers));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false);

        var late = new PlayerEntity("Atrasado", Guid.NewGuid(), group.Id, 0m, false, false, Status.Active);
        db.Players.Add(late);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await sut.SyncPlayersFromGroupAsync(group.Id, match.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        var mps = await db.MatchPlayers.AsNoTracking().Where(mp => mp.MatchId == match.Id).ToListAsync();
        mps.Should().HaveCount(3);
        mps.Should().Contain(mp => mp.PlayerId == late.Id);
    }

    // =========================
    // ADD GUEST — validação extra
    // =========================

    [Fact]
    public async Task AddGuestToMatchAsync_WithBlankName_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestToMatchAsync_WithBlankName_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Acceptation, acceptAllInvites: false);

        var result = await sut.AddGuestToMatchAsync(group.Id, match.Id, new AddGuestToMatchDto("   ", false), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Nome do convidado é obrigatório.");
    }

    // =========================
    // PLAYER HISTORY / RECENT MATCHES
    // =========================

    [Fact]
    public async Task GetPlayerRecentMatchesAsync_WhenNoFinalizedMatches_ShouldReturnEmpty()
    {
        await using var db = DbContextFactory.Create(nameof(GetPlayerRecentMatchesAsync_WhenNoFinalizedMatches_ShouldReturnEmpty));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 1);

        var result = await sut.GetPlayerRecentMatchesAsync(group.Id, players[0].Id, 5, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPlayerRecentMatchesAsync_ShouldCountGoalsAssistsAndMvp()
    {
        await using var db = DbContextFactory.Create(nameof(GetPlayerRecentMatchesAsync_ShouldCountGoalsAssistsAndMvp));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, players) = await SeedMatchAsync(db, group.Id, 4, MatchStatus.PostGame);

        // players[0] e players[1] no time A
        await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[0].Id, players[1].Id, "00:10"), CancellationToken.None);
        await sut.AddGoalAsync(group.Id, match.Id, new AddGoalRequestDto(players[0].Id, null, "00:20"), CancellationToken.None);

        // Todos votam em players[0] (exceto ele mesmo, que vota em players[1])
        var mpByPid = match.Players.ToDictionary(p => p.PlayerId, p => p);
        await sut.VoteAsync(group.Id, match.Id, mpByPid[players[0].Id].Id, mpByPid[players[1].Id].Id, CancellationToken.None);
        await sut.VoteAsync(group.Id, match.Id, mpByPid[players[1].Id].Id, mpByPid[players[0].Id].Id, CancellationToken.None);
        await sut.VoteAsync(group.Id, match.Id, mpByPid[players[2].Id].Id, mpByPid[players[0].Id].Id, CancellationToken.None);
        await sut.VoteAsync(group.Id, match.Id, mpByPid[players[3].Id].Id, mpByPid[players[0].Id].Id, CancellationToken.None);

        await sut.FinalizeMatchAsync(group.Id, match.Id, CancellationToken.None);

        var result = await sut.GetPlayerRecentMatchesAsync(group.Id, players[0].Id, 5, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        var row = result.Data![0];
        row.MatchId.Should().Be(match.Id);
        row.PlayerGoals.Should().Be(2);
        row.PlayerTeam.Should().Be(1);
        row.IsPlayerMvp.Should().BeTrue();

        var assists = await sut.GetPlayerRecentMatchesAsync(group.Id, players[1].Id, 5, CancellationToken.None);
        assists.Data![0].PlayerAssists.Should().Be(1);
    }

    [Fact]
    public async Task GetPlayerHistoryAsync_ShouldFilterByYear()
    {
        await using var db = DbContextFactory.Create(nameof(GetPlayerHistoryAsync_ShouldFilterByYear));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);

        var thisYear = new DateTime(DateTime.UtcNow.Year, 3, 15, 12, 0, 0, DateTimeKind.Utc);
        var lastYear = thisYear.AddYears(-1);

        var (mNow, playersNow) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Finalized, setScoreInPostGame: true, playedAtUtc: thisYear);

        // O mesmo jogador participa de outra partida no ano anterior
        var match = new MatchEntity(group.Id, lastYear, "Antigo Campo");
        db.Matches.Add(match);
        var mpOld = new MatchPlayerEntity(playersNow[0].Id);
        match.AddPlayer(mpOld, playersNow[0]);
        var other = new PlayerEntity("Outro", Guid.NewGuid(), group.Id, 0m, false, false, Status.Active);
        db.Players.Add(other);
        var mpOther = new MatchPlayerEntity(other.Id);
        match.AddPlayer(mpOther, other);
        match.OpenAcceptation();
        match.AcceptInvite(playersNow[0].Id);
        match.AcceptInvite(other.Id);
        match.GoToMatchMaking();
        match.AssignTeams(new[] { playersNow[0].Id }, new[] { other.Id });
        match.Start();
        match.End();
        match.GoToPostGame();
        match.SetScore(0, 0);
        match.FinalizeByVotes();
        await db.SaveChangesAsync();

        var all = await sut.GetPlayerHistoryAsync(group.Id, playersNow[0].Id, null, CancellationToken.None);
        var filtered = await sut.GetPlayerHistoryAsync(group.Id, playersNow[0].Id, thisYear.Year, CancellationToken.None);

        all.Data.Should().HaveCount(2);
        filtered.Data.Should().HaveCount(1);
        filtered.Data![0].MatchId.Should().Be(mNow.Id);
    }

    // =========================
    // REPLAYS — likes / favorites / listing / upload
    // =========================

    [Fact]
    public async Task ToggleLikeAsync_ShouldLikeThenUnlike()
    {
        await using var db = DbContextFactory.Create(nameof(ToggleLikeAsync_ShouldLikeThenUnlike));
        var sut = CreateSut(db);

        var clip = MakeClip(Guid.NewGuid(), Guid.NewGuid());
        db.ReplayClips.Add(clip);
        await db.SaveChangesAsync();

        var userId = Guid.NewGuid();

        var (isLiked, count) = await sut.ToggleLikeAsync(clip.Id, userId, CancellationToken.None);
        isLiked.Should().BeTrue();
        count.Should().Be(1);

        var (isLiked2, count2) = await sut.ToggleLikeAsync(clip.Id, userId, CancellationToken.None);
        isLiked2.Should().BeFalse();
        count2.Should().Be(0);
    }

    [Fact]
    public async Task ToggleFavoriteAsync_ShouldFavoriteThenUnfavorite()
    {
        await using var db = DbContextFactory.Create(nameof(ToggleFavoriteAsync_ShouldFavoriteThenUnfavorite));
        var sut = CreateSut(db);

        var clip = MakeClip(Guid.NewGuid(), Guid.NewGuid());
        db.ReplayClips.Add(clip);
        await db.SaveChangesAsync();

        var userId = Guid.NewGuid();

        (await sut.ToggleFavoriteAsync(clip.Id, userId, CancellationToken.None)).Should().BeTrue();
        (await db.ReplayFavorites.AsNoTracking().CountAsync(f => f.ClipId == clip.Id)).Should().Be(1);

        (await sut.ToggleFavoriteAsync(clip.Id, userId, CancellationToken.None)).Should().BeFalse();
        (await db.ReplayFavorites.AsNoTracking().CountAsync(f => f.ClipId == clip.Id)).Should().Be(0);
    }

    [Fact]
    public async Task GetClipLikersAsync_ShouldReturnLikersOrderedByMostRecent()
    {
        await using var db = DbContextFactory.Create(nameof(GetClipLikersAsync_ShouldReturnLikersOrderedByMostRecent));
        var sut = CreateSut(db);

        var clip = MakeClip(Guid.NewGuid(), Guid.NewGuid());
        db.ReplayClips.Add(clip);

        var u1 = await SeedUserAsync(db, "alice");
        var u2 = await SeedUserAsync(db, "bob");

        await sut.ToggleLikeAsync(clip.Id, u1.Id, CancellationToken.None);
        await Task.Delay(10);
        await sut.ToggleLikeAsync(clip.Id, u2.Id, CancellationToken.None);

        var result = await sut.GetClipLikersAsync(clip.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data![0].UserName.Should().Be("bob");   // mais recente primeiro
        result.Data![1].UserName.Should().Be("alice");
    }

    [Fact]
    public async Task GetLikedReplaysAsync_WhenNoClips_ShouldReturnEmptyPage()
    {
        await using var db = DbContextFactory.Create(nameof(GetLikedReplaysAsync_WhenNoClips_ShouldReturnEmptyPage));
        var sut = CreateSut(db);

        var result = await sut.GetLikedReplaysAsync(Guid.NewGuid(), null, 1, 10, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
        result.Data!.Total.Should().Be(0);
    }

    [Fact]
    public async Task GetLikedReplaysAsync_ShouldOrderByLikeCountDesc()
    {
        await using var db = DbContextFactory.Create(nameof(GetLikedReplaysAsync_ShouldOrderByLikeCountDesc));
        var sut = CreateSut(db);

        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        var popular = MakeClip(groupId, matchId, "popular.mp4");
        var lessPopular = MakeClip(groupId, matchId, "less.mp4");
        var noLikes = MakeClip(groupId, matchId, "nolikes.mp4");
        db.ReplayClips.AddRange(popular, lessPopular, noLikes);
        await db.SaveChangesAsync();

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await sut.ToggleLikeAsync(popular.Id, userA, CancellationToken.None);
        await sut.ToggleLikeAsync(popular.Id, userB, CancellationToken.None);
        await sut.ToggleLikeAsync(lessPopular.Id, userA, CancellationToken.None);

        var result = await sut.GetLikedReplaysAsync(groupId, userA, 1, 10, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Total.Should().Be(2); // clip sem likes fica de fora
        result.Data!.Items[0].Id.Should().Be(popular.Id);
        result.Data!.Items[0].LikeCount.Should().Be(2);
        result.Data!.Items[1].Id.Should().Be(lessPopular.Id);
        result.Data!.Items[0].IsLikedByMe.Should().BeTrue();
    }

    [Fact]
    public async Task GetLikedReplaysAsync_WhenPageBeyondData_ShouldReturnEmptyItems()
    {
        await using var db = DbContextFactory.Create(nameof(GetLikedReplaysAsync_WhenPageBeyondData_ShouldReturnEmptyItems));
        var sut = CreateSut(db);

        var groupId = Guid.NewGuid();
        var clip = MakeClip(groupId, Guid.NewGuid());
        db.ReplayClips.Add(clip);
        await db.SaveChangesAsync();
        await sut.ToggleLikeAsync(clip.Id, Guid.NewGuid(), CancellationToken.None);

        var result = await sut.GetLikedReplaysAsync(groupId, null, 5, 10, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
        result.Data!.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetAllGroupReplaysAsync_ShouldPaginateOrderedByRecordedAtDesc()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllGroupReplaysAsync_ShouldPaginateOrderedByRecordedAtDesc));
        var sut = CreateSut(db);

        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.ReplayClips.AddRange(
            MakeClip(groupId, matchId, "old.mp4", recordedAt: now.AddMinutes(-30)),
            MakeClip(groupId, matchId, "mid.mp4", recordedAt: now.AddMinutes(-20)),
            MakeClip(groupId, matchId, "new.mp4", recordedAt: now.AddMinutes(-10)),
            MakeClip(Guid.NewGuid(), matchId, "othergroup.mp4"));
        await db.SaveChangesAsync();

        var page1 = await sut.GetAllGroupReplaysAsync(groupId, Guid.NewGuid(), 1, 2, CancellationToken.None);

        page1.Success.Should().BeTrue();
        page1.Data!.Total.Should().Be(3);
        page1.Data!.Items.Should().HaveCount(2);
        page1.Data!.Items[0].ObjectKey.Should().Be("new.mp4");
        page1.Data!.Items[1].ObjectKey.Should().Be("mid.mp4");

        var page2 = await sut.GetAllGroupReplaysAsync(groupId, Guid.NewGuid(), 2, 2, CancellationToken.None);
        page2.Data!.Items.Should().ContainSingle(i => i.ObjectKey == "old.mp4");
    }

    [Fact]
    public async Task GetMyLikesAsync_ShouldReturnOnlyClipsLikedByUser()
    {
        await using var db = DbContextFactory.Create(nameof(GetMyLikesAsync_ShouldReturnOnlyClipsLikedByUser));
        var sut = CreateSut(db);

        var groupId = Guid.NewGuid();
        var mine = MakeClip(groupId, Guid.NewGuid(), "mine.mp4");
        var others = MakeClip(groupId, Guid.NewGuid(), "others.mp4");
        db.ReplayClips.AddRange(mine, others);
        await db.SaveChangesAsync();

        var me = Guid.NewGuid();
        await sut.ToggleLikeAsync(mine.Id, me, CancellationToken.None);
        await sut.ToggleLikeAsync(others.Id, Guid.NewGuid(), CancellationToken.None);

        var result = await sut.GetMyLikesAsync(groupId, me, 1, 10, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Total.Should().Be(1);
        result.Data!.Items.Should().ContainSingle(i => i.Id == mine.Id && i.IsLikedByMe);
    }

    [Fact]
    public async Task GetMyFavoritesAsync_ShouldReturnOnlyClipsFavoritedByUser()
    {
        await using var db = DbContextFactory.Create(nameof(GetMyFavoritesAsync_ShouldReturnOnlyClipsFavoritedByUser));
        var sut = CreateSut(db);

        var groupId = Guid.NewGuid();
        var fav = MakeClip(groupId, Guid.NewGuid(), "fav.mp4");
        var notFav = MakeClip(groupId, Guid.NewGuid(), "notfav.mp4");
        db.ReplayClips.AddRange(fav, notFav);
        await db.SaveChangesAsync();

        var me = Guid.NewGuid();
        await sut.ToggleFavoriteAsync(fav.Id, me, CancellationToken.None);

        var result = await sut.GetMyFavoritesAsync(groupId, me, 1, 10, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Total.Should().Be(1);
        result.Data!.Items.Should().ContainSingle(i => i.Id == fav.Id && i.IsFavoritedByMe);
    }

    [Fact]
    public async Task GetReplayClipAsync_ShouldReturnClipOrNull()
    {
        await using var db = DbContextFactory.Create(nameof(GetReplayClipAsync_ShouldReturnClipOrNull));
        var sut = CreateSut(db);

        var groupId = Guid.NewGuid();
        var clip = MakeClip(groupId, Guid.NewGuid());
        db.ReplayClips.Add(clip);
        await db.SaveChangesAsync();

        (await sut.GetReplayClipAsync(groupId, clip.Id, CancellationToken.None)).Should().NotBeNull();
        (await sut.GetReplayClipAsync(groupId, Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
        (await sut.GetReplayClipAsync(Guid.NewGuid(), clip.Id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteReplayAsync_WhenClipNotFound_ShouldBeNoOp()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteReplayAsync_WhenClipNotFound_ShouldBeNoOp));
        var urls = new Mock<IReplayUrlService>();
        var sut = CreateSut(db, urls.Object);

        await sut.DeleteReplayAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        urls.Verify(u => u.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteReplayAsync_ShouldRemoveClipLikesAndFavorites_AndDeleteFromStorage()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteReplayAsync_ShouldRemoveClipLikesAndFavorites_AndDeleteFromStorage));
        var urls = new Mock<IReplayUrlService>();
        var sut = CreateSut(db, urls.Object);

        var groupId = Guid.NewGuid();
        var clip = MakeClip(groupId, Guid.NewGuid(), "todelete.mp4");
        db.ReplayClips.Add(clip);
        db.ReplayLikes.Add(new ReplayLikeEntity(clip.Id, Guid.NewGuid()));
        db.ReplayFavorites.Add(new ReplayFavoriteEntity(clip.Id, Guid.NewGuid()));
        await db.SaveChangesAsync();

        await sut.DeleteReplayAsync(groupId, clip.Id, CancellationToken.None);

        (await db.ReplayClips.AsNoTracking().AnyAsync(c => c.Id == clip.Id)).Should().BeFalse();
        (await db.ReplayLikes.AsNoTracking().AnyAsync(l => l.ClipId == clip.Id)).Should().BeFalse();
        (await db.ReplayFavorites.AsNoTracking().AnyAsync(f => f.ClipId == clip.Id)).Should().BeFalse();
        urls.Verify(u => u.DeleteObjectAsync("todelete.mp4", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteReplayAsync_WhenStorageDeleteFails_ShouldStillRemoveFromDb()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteReplayAsync_WhenStorageDeleteFails_ShouldStillRemoveFromDb));
        var urls = new Mock<IReplayUrlService>();
        urls.Setup(u => u.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("R2 down"));
        var sut = CreateSut(db, urls.Object);

        var groupId = Guid.NewGuid();
        var clip = MakeClip(groupId, Guid.NewGuid());
        db.ReplayClips.Add(clip);
        await db.SaveChangesAsync();

        await sut.DeleteReplayAsync(groupId, clip.Id, CancellationToken.None);

        (await db.ReplayClips.AsNoTracking().AnyAsync(c => c.Id == clip.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task UploadReplayAsync_WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(UploadReplayAsync_WhenMatchNotFound_ShouldFail));
        var sut = CreateSut(db);

        using var stream = new MemoryStream([1, 2, 3]);
        var result = await sut.UploadReplayAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            stream, "video/mp4", "clip.mp4", "Gol", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Partida não encontrada.");
    }

    [Fact]
    public async Task UploadReplayAsync_WithInvalidEventType_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(UploadReplayAsync_WithInvalidEventType_ShouldFail));
        var sut = CreateSut(db);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Started);

        using var stream = new MemoryStream([1, 2, 3]);
        var result = await sut.UploadReplayAsync(
            group.Id, match.Id, Guid.NewGuid(),
            stream, "video/mp4", "clip.mp4", "Golaço", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Tipo de evento inválido. Use 'Gol' ou 'Jogada'.");
    }

    [Fact]
    public async Task UploadReplayAsync_WhenStorageUploadFails_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(UploadReplayAsync_WhenStorageUploadFails_ShouldFail));
        var urls = new Mock<IReplayUrlService>();
        urls.Setup(u => u.UploadObjectAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no S3"));
        var sut = CreateSut(db, urls.Object);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Started);

        using var stream = new MemoryStream([1, 2, 3]);
        var result = await sut.UploadReplayAsync(
            group.Id, match.Id, Guid.NewGuid(),
            stream, "video/mp4", "clip.mp4", "Gol", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Falha ao enviar vídeo para o storage. Tente novamente.");
        (await db.ReplayClips.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task UploadReplayAsync_WhenValid_ShouldPersistClipAndReturnDto()
    {
        await using var db = DbContextFactory.Create(nameof(UploadReplayAsync_WhenValid_ShouldPersistClipAndReturnDto));
        var urls = new Mock<IReplayUrlService>();
        urls.SetupGet(u => u.BucketName).Returns("goal-replays");
        urls.Setup(u => u.UploadObjectAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-123");
        urls.Setup(u => u.GeneratePresignedUrl(It.IsAny<string>()))
            .Returns("https://r2.example.com/presigned");
        var sut = CreateSut(db, urls.Object);

        var group = await SeedGroupAsync(db);
        var (match, _) = await SeedMatchAsync(db, group.Id, 2, MatchStatus.Started);

        using var stream = new MemoryStream([1, 2, 3]);
        var result = await sut.UploadReplayAsync(
            group.Id, match.Id, Guid.NewGuid(),
            stream, "video/mp4", "lance.exe", "jogada", CancellationToken.None);

        result.Success.Should().BeTrue();
        var dto = result.Data!;
        dto.EventType.Should().Be("Jogada");
        dto.VideoUrl.Should().Be("https://r2.example.com/presigned");
        dto.LikeCount.Should().Be(0);
        // extensão desconhecida (.exe) vira .mp4 e vai para a subpasta "jogadas"
        dto.ObjectKey.Should().StartWith($"{group.Id}/{match.Id}/jogadas/");
        dto.ObjectKey.Should().EndWith(".mp4");

        (await db.ReplayClips.AsNoTracking().CountAsync()).Should().Be(1);
    }

    // =========================
    // CURRENT — empty groupId guard
    // =========================

    [Fact]
    public async Task GetCurrentAsync_WhenEmptyGroupId_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(GetCurrentAsync_WhenEmptyGroupId_ShouldFail));
        var sut = CreateSut(db);

        var result = await sut.GetCurrentAsync(Guid.Empty, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("GroupId é obrigatório.");
    }
}
