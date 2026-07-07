using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Tests that GetPollsAsync and GetPollAsync surface LinkedMatchId
/// in the returned DTOs — for both regular polls and event-type polls.
/// </summary>
public sealed class LinkedPollServiceTests
{
    // ── Factory ───────────────────────────────────────────────────────────────

    private static PollService CreateSut(AppDbContext db) =>
        new(db,
            Mock.Of<IPushService>(),
            Mock.Of<INotificationScheduler>(),
            Mock.Of<ILogger<PollService>>());

    private static PollEntity MakePoll(Guid groupId, string type = "poll") =>
        new(groupId, "Votação teste", null,
            allowMultipleVotes: false,
            showVotes: false,
            createdByUserId: Guid.NewGuid(),
            type: type);

    // ── GetPollsAsync — LinkedMatchId in PollSummaryDto ───────────────────────

    [Fact]
    public async Task GetPollsAsync_WhenPollHasLinkedMatch_ShouldIncludeLinkedMatchId()
    {
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(GetPollsAsync_WhenPollHasLinkedMatch_ShouldIncludeLinkedMatchId));
        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        var poll = MakePoll(groupId);
        poll.SetLinkedMatch(matchId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetPollsAsync(groupId, Guid.NewGuid());

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().ContainSingle()
            .Which.LinkedMatchId.Should().Be(matchId);
    }

    [Fact]
    public async Task GetPollsAsync_WhenPollHasNoLinkedMatch_LinkedMatchIdShouldBeNull()
    {
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(GetPollsAsync_WhenPollHasNoLinkedMatch_LinkedMatchIdShouldBeNull));
        var groupId = Guid.NewGuid();

        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetPollsAsync(groupId, Guid.NewGuid());

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().ContainSingle()
            .Which.LinkedMatchId.Should().BeNull();
    }

    [Fact]
    public async Task GetPollsAsync_WhenEventHasLinkedMatch_ShouldIncludeLinkedMatchId()
    {
        // Arrange — eventos (type="event") também devem expor LinkedMatchId
        await using var db = DbContextFactory.Create(
            nameof(GetPollsAsync_WhenEventHasLinkedMatch_ShouldIncludeLinkedMatchId));
        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        var eventPoll = MakePoll(groupId, type: "event");
        eventPoll.SetLinkedMatch(matchId);
        db.Polls.Add(eventPoll);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetPollsAsync(groupId, Guid.NewGuid());

        // Assert
        result.Success.Should().BeTrue();
        var dto = result.Data!.Items.Single();
        dto.Type.Should().Be("event");
        dto.LinkedMatchId.Should().Be(matchId);
    }

    [Fact]
    public async Task GetPollsAsync_WithMixedPolls_ShouldReturnCorrectLinkedMatchIds()
    {
        // Arrange — um poll vinculado, outro não
        await using var db = DbContextFactory.Create(
            nameof(GetPollsAsync_WithMixedPolls_ShouldReturnCorrectLinkedMatchIds));
        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        var linked   = MakePoll(groupId);
        linked.SetLinkedMatch(matchId);

        var unlinked = MakePoll(groupId);

        db.Polls.AddRange(linked, unlinked);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetPollsAsync(groupId, Guid.NewGuid());

        // Assert
        result.Data!.Items.Should().HaveCount(2);
        result.Data!.Items.Single(p => p.Id == linked.Id).LinkedMatchId.Should().Be(matchId);
        result.Data!.Items.Single(p => p.Id == unlinked.Id).LinkedMatchId.Should().BeNull();
    }

    [Fact]
    public async Task GetPollsAsync_LinkedMatchIdUpdatedToNull_ShouldReturnNull()
    {
        // Arrange — poll foi vinculado e depois desvinculado
        await using var db = DbContextFactory.Create(
            nameof(GetPollsAsync_LinkedMatchIdUpdatedToNull_ShouldReturnNull));
        var groupId = Guid.NewGuid();

        var poll = MakePoll(groupId);
        poll.SetLinkedMatch(Guid.NewGuid());
        poll.SetLinkedMatch(null); // unlink
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetPollsAsync(groupId, Guid.NewGuid());

        // Assert
        result.Data!.Items.Single().LinkedMatchId.Should().BeNull();
    }

    // ── GetPollAsync — LinkedMatchId in PollDto ───────────────────────────────

    [Fact]
    public async Task GetPollAsync_WhenPollHasLinkedMatch_ShouldIncludeLinkedMatchId()
    {
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(GetPollAsync_WhenPollHasLinkedMatch_ShouldIncludeLinkedMatchId));
        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        var poll = MakePoll(groupId);
        poll.SetLinkedMatch(matchId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetPollAsync(
            groupId, poll.Id, playerId: Guid.NewGuid(), isAdmin: false);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.LinkedMatchId.Should().Be(matchId);
    }

    [Fact]
    public async Task GetPollAsync_WhenPollHasNoLinkedMatch_LinkedMatchIdShouldBeNull()
    {
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(GetPollAsync_WhenPollHasNoLinkedMatch_LinkedMatchIdShouldBeNull));
        var groupId = Guid.NewGuid();

        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetPollAsync(
            groupId, poll.Id, playerId: Guid.NewGuid(), isAdmin: false);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.LinkedMatchId.Should().BeNull();
    }

    [Fact]
    public async Task GetPollAsync_WhenEventHasLinkedMatch_ShouldIncludeLinkedMatchId()
    {
        // Arrange
        await using var db = DbContextFactory.Create(
            nameof(GetPollAsync_WhenEventHasLinkedMatch_ShouldIncludeLinkedMatchId));
        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        var eventPoll = MakePoll(groupId, type: "event");
        eventPoll.SetLinkedMatch(matchId);
        db.Polls.Add(eventPoll);
        // Event polls need at least one option (Sim/Talvez/Não)
        db.PollOptions.Add(new PollOptionEntity(eventPoll.Id, "Sim",  null, null, 0));
        db.PollOptions.Add(new PollOptionEntity(eventPoll.Id, "Não",  null, null, 1));
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetPollAsync(
            groupId, eventPoll.Id, playerId: Guid.NewGuid(), isAdmin: false);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Type.Should().Be("event");
        result.Data!.LinkedMatchId.Should().Be(matchId);
    }

    [Fact]
    public async Task GetPollAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(
            nameof(GetPollAsync_WhenPollNotFound_ShouldReturnFailure));

        var sut    = CreateSut(db);
        var result = await sut.GetPollAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("não encontrada");
    }
}
