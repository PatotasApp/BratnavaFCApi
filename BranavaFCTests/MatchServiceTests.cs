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
    // Repo mock (escreve no MESMO db do SUT)
    // =========================
    private static Mock<IRepositoryBase<MatchEntity>> BuildRepoMock(AppDbContext db)
    {
        var repo = new Mock<IRepositoryBase<MatchEntity>>(MockBehavior.Strict);

        repo.Setup(r => r.Add(It.IsAny<MatchEntity>()))
            .Callback<MatchEntity>(m => db.Matches.Add(m));

        repo.Setup(r => r.Remove(It.IsAny<MatchEntity>()))
            .Callback<MatchEntity>(m => db.Matches.Remove(m));

        repo.Setup(r => r.Update(It.IsAny<MatchEntity>()))
            .Callback<MatchEntity>(m => db.Matches.Update(m));

        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct => db.SaveChangesAsync(ct));

        // Se sua interface não tiver esses métodos, remova.
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchEntity?)null);

        repo.Setup(r => r.GetByIdIncludingInactiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchEntity?)null);

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

    // =========================
    // MemberData helpers
    // =========================

    public static IEnumerable<object[]> StatusNotCreated()
    {
        yield return new object[] { MatchStatus.Started };
        yield return new object[] { MatchStatus.Ended };
        yield return new object[] { MatchStatus.Finalized };
    }

    public static IEnumerable<object[]> StatusNotStarted()
    {
        yield return new object[] { MatchStatus.Created };
        yield return new object[] { MatchStatus.Ended };
        yield return new object[] { MatchStatus.Finalized };
    }

    public static IEnumerable<object[]> StatusNotEnded()
    {
        yield return new object[] { MatchStatus.Created };
        yield return new object[] { MatchStatus.Started };
        yield return new object[] { MatchStatus.Finalized };
    }

    // ============================================================
    // ✅ TESTES DE STATUS (não pode executar ação em status inválido)
    // ============================================================

    [Theory]
    [MemberData(nameof(StatusNotCreated))]
    public async Task AcceptInviteAsync_WhenStatusNotCreated_ShouldThrow(MatchStatus status)
    {
        await using var db = DbContextFactory.Create(nameof(AcceptInviteAsync_WhenStatusNotCreated_ShouldThrow) + "_" + status);
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 2);
        var match = await SeedMatchWithPlayersAsync(db, group.Id, players, status);

        Func<Task> act = () => sut.AcceptInviteAsync(group.Id, match.Id, players[0].Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Só é possível aceitar convite quando a partida está Criada.");
    }

    [Theory]
    [MemberData(nameof(StatusNotCreated))]
    public async Task RejectInviteAsync_WhenStatusNotCreated_ShouldThrow(MatchStatus status)
    {
        await using var db = DbContextFactory.Create(nameof(RejectInviteAsync_WhenStatusNotCreated_ShouldThrow) + "_" + status);
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 2);
        var match = await SeedMatchWithPlayersAsync(db, group.Id, players, status);

        Func<Task> act = () => sut.RejectInviteAsync(group.Id, match.Id, players[0].Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Só é possível recusar convite quando a partida está Criada.");
    }

    [Theory]
    [MemberData(nameof(StatusNotCreated))]
    public async Task AssignTeamsAsync_WhenStatusNotCreated_ShouldThrow(MatchStatus status)
    {
        await using var db = DbContextFactory.Create(nameof(AssignTeamsAsync_WhenStatusNotCreated_ShouldThrow) + "_" + status);
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 2);
        var match = await SeedMatchWithPlayersAsync(db, group.Id, players, status);

        var dto = new AssignTeamsDto
        {
            TeamAMatchPlayerIds = match.TeamAPlayers.Select(x => x.PlayerId).ToList(),
            TeamBMatchPlayerIds = match.TeamBPlayers.Select(x => x.PlayerId).ToList()
        };

        Func<Task> act = () => sut.AssignTeamsAsync(group.Id, match.Id, dto, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Só é possível atribuir times quando a partida está Criada.");
    }

    [Theory]
    [MemberData(nameof(StatusNotCreated))]
    public async Task SwapPlayersByPlayerIdAsync_WhenStatusNotCreated_ShouldThrow(MatchStatus status)
    {
        await using var db = DbContextFactory.Create(nameof(SwapPlayersByPlayerIdAsync_WhenStatusNotCreated_ShouldThrow) + "_" + status);
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 2);
        var match = await SeedMatchWithPlayersAsync(db, group.Id, players, status);

        Func<Task> act = () => sut.SwapPlayersByPlayerIdAsync(group.Id, match.Id, players[0].Id, players[1].Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Só é possível trocar jogadores quando a partida está Criada.");
    }

    [Theory]
    [MemberData(nameof(StatusNotCreated))]
    public async Task StartMatchAsync_WhenStatusNotCreated_ShouldThrow(MatchStatus status)
    {
        await using var db = DbContextFactory.Create(nameof(StartMatchAsync_WhenStatusNotCreated_ShouldThrow) + "_" + status);
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 2);
        var match = await SeedMatchWithPlayersAsync(db, group.Id, players, status);

        Func<Task> act = () => sut.StartMatchAsync(group.Id, match.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("A partida só pode ser iniciada se estiver Criada.");
    }

    [Theory]
    [MemberData(nameof(StatusNotStarted))]
    public async Task EndMatchAsync_WhenStatusNotStarted_ShouldThrow(MatchStatus status)
    {
        await using var db = DbContextFactory.Create(nameof(EndMatchAsync_WhenStatusNotStarted_ShouldThrow) + "_" + status);
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 2);

        // se status=Ended/Finalized precisa seedar coerente
        var match = status switch
        {
            MatchStatus.Created => await SeedMatchWithPlayersAsync(db, group.Id, players, MatchStatus.Created, teamsDefined: true),
            MatchStatus.Ended => await SeedMatchWithPlayersAsync(db, group.Id, players, MatchStatus.Ended, teamsDefined: true),
            MatchStatus.Finalized => await SeedMatchWithPlayersAsync(db, group.Id, players, MatchStatus.Finalized, teamsDefined: true, withScore: true, withVotes: true, finalized: true),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        Func<Task> act = () => sut.EndMatchAsync(group.Id, match.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("A partida só pode ser encerrada se estiver Iniciada.");
    }

    [Theory]
    [MemberData(nameof(StatusNotEnded))]
    public async Task SetScoreAsync_WhenStatusNotEnded_ShouldThrow(MatchStatus status)
    {
        await using var db = DbContextFactory.Create(nameof(SetScoreAsync_WhenStatusNotEnded_ShouldThrow) + "_" + status);
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 2);

        var match = status switch
        {
            MatchStatus.Created => await SeedMatchWithPlayersAsync(db, group.Id, players, MatchStatus.Created),
            MatchStatus.Started => await SeedMatchWithPlayersAsync(db, group.Id, players, MatchStatus.Started),
            MatchStatus.Finalized => await SeedMatchWithPlayersAsync(db, group.Id, players, MatchStatus.Finalized, withScore: true, withVotes: true, finalized: true),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        Func<Task> act = () => sut.SetScoreAsync(group.Id, match.Id, 1, 0, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Só é possível setar placar quando a partida está Encerrada.");
    }

    [Theory]
    [MemberData(nameof(StatusNotEnded))]
    public async Task VoteAsync_WhenStatusNotEnded_ShouldThrow(MatchStatus status)
    {
        await using var db = DbContextFactory.Create(nameof(VoteAsync_WhenStatusNotEnded_ShouldThrow) + "_" + status);
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 2);

        var match = status switch
        {
            MatchStatus.Created => await SeedMatchWithPlayersAsync(db, group.Id, players, MatchStatus.Created),
            MatchStatus.Started => await SeedMatchWithPlayersAsync(db, group.Id, players, MatchStatus.Started),
            MatchStatus.Finalized => await SeedMatchWithPlayersAsync(db, group.Id, players, MatchStatus.Finalized, withScore: true, withVotes: true, finalized: true),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        var voterMpId = match.Players[0].Id;
        var votedMpId = match.Players[1].Id;

        Func<Task> act = () => sut.VoteAsync(group.Id, match.Id, voterMpId, votedMpId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Só é possível votar no MVP quando a partida está Encerrada.");
    }

    [Theory]
    [MemberData(nameof(StatusNotEnded))]
    public async Task FinalizeMatchAsync_WhenStatusNotEnded_ShouldThrow(MatchStatus status)
    {
        await using var db = DbContextFactory.Create(nameof(FinalizeMatchAsync_WhenStatusNotEnded_ShouldThrow) + "_" + status);
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 2);

        var match = status switch
        {
            MatchStatus.Created => await SeedMatchWithPlayersAsync(db, group.Id, players, MatchStatus.Created),
            MatchStatus.Started => await SeedMatchWithPlayersAsync(db, group.Id, players, MatchStatus.Started),
            MatchStatus.Finalized => await SeedMatchWithPlayersAsync(db, group.Id, players, MatchStatus.Finalized, withScore: true, withVotes: true, finalized: true),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        Func<Task> act = () => sut.FinalizeMatchAsync(group.Id, match.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("A partida só pode ser finalizada se estiver Encerrada.");
    }

    // =========================
    // ✅ CORREÇÃO: SetTeamColors status
    // =========================
    [Theory]
    [MemberData(nameof(StatusNotCreated))]
    public async Task SetTeamColorsAsync_WhenStatusNotCreated_ShouldThrow(MatchStatus status)
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(SetTeamColorsAsync_WhenStatusNotCreated_ShouldThrow) + "_" + status);
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 2);

        var match = await SeedMatchWithPlayersAsync(
            db,
            group.Id,
            players,
            status,
            acceptedInvites: true,
            teamsDefined: true,
            withScore: status is MatchStatus.Ended or MatchStatus.Finalized,
            withVotes: status == MatchStatus.Finalized,
            finalized: status == MatchStatus.Finalized
        );

        // ✅ IMPORTANTE:
        // Para testar o "status inválido", não pode passar IDs inexistentes,
        // porque o service valida existência antes do domínio.
        // Então: (null, null, randomize=false) força cair no guard de status do domínio.
        Func<Task> act = () => sut.SetTeamColorsAsync(
            group.Id,
            match.Id,
            teamAColorId: null,
            teamBColorId: null,
            randomize: false,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Só é possível setar cores quando a partida está Criada.");
    }

    [Fact]
    public async Task SetTeamColorsAsync_WhenCreatedAndColorNotFound_ShouldThrow()
    {
        await using var db = DbContextFactory.Create(nameof(SetTeamColorsAsync_WhenCreatedAndColorNotFound_ShouldThrow));
        var repo = BuildRepoMock(db);
        var sut = CreateSut(db, repo);

        var group = await SeedGroupAsync(db);
        var players = await SeedPlayersAsync(db, group.Id, 2);
        var match = await SeedMatchWithPlayersAsync(db, group.Id, players, MatchStatus.Created);

        Func<Task> act = () => sut.SetTeamColorsAsync(group.Id, match.Id, Guid.NewGuid(), null, false, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cor do time A não encontrada.");
    }

}
