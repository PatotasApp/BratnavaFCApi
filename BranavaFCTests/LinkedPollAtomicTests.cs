using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Tests the atomic bidirectional Poll↔Match linking logic implemented in
/// MatchService.SetLinkedPollAsync (exposed via MatchesController.SetLinkedPoll).
/// </summary>
public sealed class LinkedPollAtomicTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MatchEntity MakeMatch(Guid groupId) =>
        new(groupId, DateTime.UtcNow.AddDays(1), "Arena");

    private static PollEntity MakePoll(Guid groupId) =>
        new(groupId, "Votação", null, false, false, null);

    /// <summary>Executa o método real do service; retorna Success.</summary>
    private static async Task<bool> Execute(
        AppDbContext db,
        Guid         groupId,
        Guid         matchId,
        Guid?        pollId,
        CancellationToken ct = default)
    {
        var sut = new MatchService(db, new RepositoryBase<MatchEntity>(db),
            Mock.Of<IPushService>(), Mock.Of<IReplayUrlService>(),
            Mock.Of<IBetService>(), Mock.Of<INotificationScheduler>(), TestImageStorage.Create());

        var result = await sut.SetLinkedPollAsync(groupId, matchId, pollId, ct);
        return result.Success;
    }

    // ── Not-found guards ──────────────────────────────────────────────────────

    [Fact]
    public async Task SetLinkedPoll_WhenMatchNotFound_ShouldReturnFalse()
    {
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenMatchNotFound_ShouldReturnFalse));

        var result = await Execute(db, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetLinkedPoll_WhenPollNotFoundInGroup_ShouldReturnFalse()
    {
        // Arrange — match exists but requested poll belongs to another group
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenPollNotFoundInGroup_ShouldReturnFalse));
        var groupId = Guid.NewGuid();
        var match   = MakeMatch(groupId);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var result = await Execute(db, groupId, match.Id, Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetLinkedPoll_WhenPollBelongsToDifferentGroup_ShouldReturnFalse()
    {
        // Arrange — poll exists but groupId doesn't match
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenPollBelongsToDifferentGroup_ShouldReturnFalse));
        var groupId      = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();
        var match = MakeMatch(groupId);
        var poll  = MakePoll(otherGroupId); // belongs to a DIFFERENT group
        db.Matches.Add(match);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var result = await Execute(db, groupId, match.Id, poll.Id);

        result.Should().BeFalse();
        var reloaded = await db.Matches.FirstAsync(m => m.Id == match.Id);
        reloaded.LinkedPollId.Should().BeNull("match must not be modified on failure");
    }

    // ── Link: happy path ──────────────────────────────────────────────────────

    [Fact]
    public async Task SetLinkedPoll_WhenLinking_ShouldSetMatchLinkedPollId()
    {
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenLinking_ShouldSetMatchLinkedPollId));
        var groupId = Guid.NewGuid();
        var match   = MakeMatch(groupId);
        var poll    = MakePoll(groupId);
        db.Matches.Add(match);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var result = await Execute(db, groupId, match.Id, poll.Id);

        result.Should().BeTrue();
        var reloaded = await db.Matches.FirstAsync(m => m.Id == match.Id);
        reloaded.LinkedPollId.Should().Be(poll.Id);
    }

    [Fact]
    public async Task SetLinkedPoll_WhenLinking_ShouldSetPollLinkedMatchId()
    {
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenLinking_ShouldSetPollLinkedMatchId));
        var groupId = Guid.NewGuid();
        var match   = MakeMatch(groupId);
        var poll    = MakePoll(groupId);
        db.Matches.Add(match);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        await Execute(db, groupId, match.Id, poll.Id);

        var reloaded = await db.Polls.FirstAsync(p => p.Id == poll.Id);
        reloaded.LinkedMatchId.Should().Be(match.Id,
            "the poll's back-reference must be set atomically");
    }

    [Fact]
    public async Task SetLinkedPoll_WhenLinking_ShouldPersistBothSidesViaSecondContext()
    {
        // Verify via an independent DbContext that both sides survive SaveChanges
        const string db_name = nameof(SetLinkedPoll_WhenLinking_ShouldPersistBothSidesViaSecondContext);
        await using var db = DbContextFactory.Create(db_name);
        var groupId = Guid.NewGuid();
        var match   = MakeMatch(groupId);
        var poll    = MakePoll(groupId);
        db.Matches.Add(match);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        await Execute(db, groupId, match.Id, poll.Id);

        await using var db2 = DbContextFactory.Create(db_name);
        var matchCheck = await db2.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        var pollCheck  = await db2.Polls.AsNoTracking().FirstAsync(p => p.Id == poll.Id);

        matchCheck.LinkedPollId.Should().Be(poll.Id);
        pollCheck.LinkedMatchId.Should().Be(match.Id);
    }

    // ── Unlink: happy path ────────────────────────────────────────────────────

    [Fact]
    public async Task SetLinkedPoll_WhenUnlinking_ShouldClearMatchLinkedPollId()
    {
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenUnlinking_ShouldClearMatchLinkedPollId));
        var groupId = Guid.NewGuid();
        var match   = MakeMatch(groupId);
        var poll    = MakePoll(groupId);
        match.SetLinkedPoll(poll.Id);
        poll.SetLinkedMatch(match.Id);
        db.Matches.Add(match);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var result = await Execute(db, groupId, match.Id, null);

        result.Should().BeTrue();
        var reloaded = await db.Matches.FirstAsync(m => m.Id == match.Id);
        reloaded.LinkedPollId.Should().BeNull();
    }

    [Fact]
    public async Task SetLinkedPoll_WhenUnlinking_ShouldClearPollLinkedMatchId()
    {
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenUnlinking_ShouldClearPollLinkedMatchId));
        var groupId = Guid.NewGuid();
        var match   = MakeMatch(groupId);
        var poll    = MakePoll(groupId);
        match.SetLinkedPoll(poll.Id);
        poll.SetLinkedMatch(match.Id);
        db.Matches.Add(match);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        await Execute(db, groupId, match.Id, null);

        var reloaded = await db.Polls.FirstAsync(p => p.Id == poll.Id);
        reloaded.LinkedMatchId.Should().BeNull(
            "the poll's back-reference must be cleared when the match is unlinked");
    }

    [Fact]
    public async Task SetLinkedPoll_UnlinkWhenNothingWasLinked_ShouldSucceedIdempotently()
    {
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_UnlinkWhenNothingWasLinked_ShouldSucceedIdempotently));
        var groupId = Guid.NewGuid();
        var match   = MakeMatch(groupId);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var result = await Execute(db, groupId, match.Id, null);

        result.Should().BeTrue();
        var reloaded = await db.Matches.FirstAsync(m => m.Id == match.Id);
        reloaded.LinkedPollId.Should().BeNull();
    }

    // ── Replace: old poll loses the link ──────────────────────────────────────

    [Fact]
    public async Task SetLinkedPoll_WhenReplacingPoll_OldPollShouldLoseLinkedMatchId()
    {
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenReplacingPoll_OldPollShouldLoseLinkedMatchId));
        var groupId = Guid.NewGuid();
        var match   = MakeMatch(groupId);
        var oldPoll = MakePoll(groupId);
        var newPoll = MakePoll(groupId);

        match.SetLinkedPoll(oldPoll.Id);
        oldPoll.SetLinkedMatch(match.Id);
        db.Matches.Add(match);
        db.Polls.AddRange(oldPoll, newPoll);
        await db.SaveChangesAsync();

        await Execute(db, groupId, match.Id, newPoll.Id);

        var reloadedOld = await db.Polls.FirstAsync(p => p.Id == oldPoll.Id);
        reloadedOld.LinkedMatchId.Should().BeNull("old poll must be detached");
    }

    [Fact]
    public async Task SetLinkedPoll_WhenReplacingPoll_NewPollShouldGainLinkedMatchId()
    {
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenReplacingPoll_NewPollShouldGainLinkedMatchId));
        var groupId = Guid.NewGuid();
        var match   = MakeMatch(groupId);
        var oldPoll = MakePoll(groupId);
        var newPoll = MakePoll(groupId);

        match.SetLinkedPoll(oldPoll.Id);
        oldPoll.SetLinkedMatch(match.Id);
        db.Matches.Add(match);
        db.Polls.AddRange(oldPoll, newPoll);
        await db.SaveChangesAsync();

        await Execute(db, groupId, match.Id, newPoll.Id);

        var reloadedNew = await db.Polls.FirstAsync(p => p.Id == newPoll.Id);
        reloadedNew.LinkedMatchId.Should().Be(match.Id, "new poll must gain the back-reference");
    }

    [Fact]
    public async Task SetLinkedPoll_WhenReplacingPoll_MatchShouldPointToNewPoll()
    {
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenReplacingPoll_MatchShouldPointToNewPoll));
        var groupId = Guid.NewGuid();
        var match   = MakeMatch(groupId);
        var oldPoll = MakePoll(groupId);
        var newPoll = MakePoll(groupId);

        match.SetLinkedPoll(oldPoll.Id);
        oldPoll.SetLinkedMatch(match.Id);
        db.Matches.Add(match);
        db.Polls.AddRange(oldPoll, newPoll);
        await db.SaveChangesAsync();

        await Execute(db, groupId, match.Id, newPoll.Id);

        var reloadedMatch = await db.Matches.FirstAsync(m => m.Id == match.Id);
        reloadedMatch.LinkedPollId.Should().Be(newPoll.Id);
    }

    // ── New poll already owned by another match ───────────────────────────────

    [Fact]
    public async Task SetLinkedPoll_WhenNewPollAlreadyLinkedElsewhere_OldMatchShouldLoseLinkedPollId()
    {
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenNewPollAlreadyLinkedElsewhere_OldMatchShouldLoseLinkedPollId));
        var groupId = Guid.NewGuid();
        var matchA  = MakeMatch(groupId); // currently owns the poll
        var matchB  = MakeMatch(groupId); // wants to claim the poll
        var poll    = MakePoll(groupId);

        matchA.SetLinkedPoll(poll.Id);
        poll.SetLinkedMatch(matchA.Id);
        db.Matches.AddRange(matchA, matchB);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        await Execute(db, groupId, matchB.Id, poll.Id);

        var reloadedA = await db.Matches.FirstAsync(m => m.Id == matchA.Id);
        reloadedA.LinkedPollId.Should().BeNull("old owner must be cleared");
    }

    [Fact]
    public async Task SetLinkedPoll_WhenNewPollAlreadyLinkedElsewhere_NewMatchShouldGainLinkedPollId()
    {
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenNewPollAlreadyLinkedElsewhere_NewMatchShouldGainLinkedPollId));
        var groupId = Guid.NewGuid();
        var matchA  = MakeMatch(groupId);
        var matchB  = MakeMatch(groupId);
        var poll    = MakePoll(groupId);

        matchA.SetLinkedPoll(poll.Id);
        poll.SetLinkedMatch(matchA.Id);
        db.Matches.AddRange(matchA, matchB);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        await Execute(db, groupId, matchB.Id, poll.Id);

        var reloadedB = await db.Matches.FirstAsync(m => m.Id == matchB.Id);
        reloadedB.LinkedPollId.Should().Be(poll.Id, "new match must now own the poll");
    }

    [Fact]
    public async Task SetLinkedPoll_WhenNewPollAlreadyLinkedElsewhere_PollShouldPointToNewMatch()
    {
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenNewPollAlreadyLinkedElsewhere_PollShouldPointToNewMatch));
        var groupId = Guid.NewGuid();
        var matchA  = MakeMatch(groupId);
        var matchB  = MakeMatch(groupId);
        var poll    = MakePoll(groupId);

        matchA.SetLinkedPoll(poll.Id);
        poll.SetLinkedMatch(matchA.Id);
        db.Matches.AddRange(matchA, matchB);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        await Execute(db, groupId, matchB.Id, poll.Id);

        var reloadedPoll = await db.Polls.FirstAsync(p => p.Id == poll.Id);
        reloadedPoll.LinkedMatchId.Should().Be(matchB.Id);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SetLinkedPoll_WhenLinkingSamePollTwice_ShouldBeIdempotent()
    {
        // Both sides must remain consistent after a redundant second call
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_WhenLinkingSamePollTwice_ShouldBeIdempotent));
        var groupId = Guid.NewGuid();
        var match   = MakeMatch(groupId);
        var poll    = MakePoll(groupId);

        match.SetLinkedPoll(poll.Id);
        poll.SetLinkedMatch(match.Id);
        db.Matches.Add(match);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        // Act — link to the same poll again
        var result = await Execute(db, groupId, match.Id, poll.Id);

        result.Should().BeTrue();
        var reloadedMatch = await db.Matches.FirstAsync(m => m.Id == match.Id);
        var reloadedPoll  = await db.Polls.FirstAsync(p => p.Id == poll.Id);

        reloadedMatch.LinkedPollId.Should().Be(poll.Id);
        reloadedPoll.LinkedMatchId.Should().Be(match.Id);
    }

    [Fact]
    public async Task SetLinkedPoll_UnlinkThenRelinkSamePoll_ShouldRestoreBothSides()
    {
        await using var db = DbContextFactory.Create(
            nameof(SetLinkedPoll_UnlinkThenRelinkSamePoll_ShouldRestoreBothSides));
        var groupId = Guid.NewGuid();
        var match   = MakeMatch(groupId);
        var poll    = MakePoll(groupId);

        match.SetLinkedPoll(poll.Id);
        poll.SetLinkedMatch(match.Id);
        db.Matches.Add(match);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        // Unlink
        await Execute(db, groupId, match.Id, null);
        // Relink
        await Execute(db, groupId, match.Id, poll.Id);

        var reloadedMatch = await db.Matches.FirstAsync(m => m.Id == match.Id);
        var reloadedPoll  = await db.Polls.FirstAsync(p => p.Id == poll.Id);

        reloadedMatch.LinkedPollId.Should().Be(poll.Id);
        reloadedPoll.LinkedMatchId.Should().Be(match.Id);
    }
}
