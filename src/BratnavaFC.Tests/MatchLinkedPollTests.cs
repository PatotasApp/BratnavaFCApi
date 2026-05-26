using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Xunit;

namespace BratnavaFC.Tests;

/// <summary>
/// Tests for the "linked poll ↔ match" feature.
///
/// Scenarios covered:
///   Entity unit tests (no DB):
///   1.  SetLinkedPoll sets LinkedPollId on MatchEntity.
///   2.  SetLinkedPoll(null) clears LinkedPollId on MatchEntity.
///   3.  SetLinkedMatch sets LinkedMatchId on PollEntity.
///   4.  SetLinkedMatch(null) clears LinkedMatchId on PollEntity.
///
///   Service projection tests (InMemory DB + MatchService):
///   5.  GetUpcomingAsync returns LinkedPollId when match has a linked poll.
///   6.  GetUpcomingAsync returns null LinkedPollId when no link exists.
///   7.  GetHeaderAsync returns LinkedPollId when linked.
///   8.  GetHeaderAsync returns null LinkedPollId when not linked.
///
///   Controller logic integration tests (EF operations directly):
///   9.  Link sets both FKs (match.LinkedPollId + poll.LinkedMatchId).
///   10. Unlink clears both FKs.
///   11. Linking a poll that is already linked to another match clears the old match.
///   12. Linking when the match already has a different poll clears the old poll.
///   13. Match lookup returns null for a random ID (404 path).
///   14. Poll lookup returns null when poll not found (400 path).
/// </summary>
public class MatchLinkedPollTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static MatchService CreateService(AppDbContext db)
    {
        var repo      = Substitute.For<IRepositoryBase<MatchEntity>>();
        var push      = Substitute.For<IPushService>();
        var replay    = Substitute.For<IReplayUrlService>();
        var bets      = Substitute.For<IBetService>();
        var scheduler = Substitute.For<INotificationScheduler>();
        return new MatchService(db, repo, push, replay, bets, scheduler);
    }

    /// <summary>
    /// Persists a GroupEntity so EnsureGroupExistsAsync passes.
    /// </summary>
    private static async Task<GroupEntity> SeedGroupAsync(AppDbContext db)
    {
        var group = new GroupEntity("Test Group", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private static PollEntity CreatePoll(Guid groupId) =>
        new(groupId, "Votação Teste", null, false, false, Guid.NewGuid());

    // ── Entity unit tests ────────────────────────────────────────────────────

    [Fact]
    public void SetLinkedPoll_SetsLinkedPollId()
    {
        var groupId = Guid.NewGuid();
        var pollId  = Guid.NewGuid();
        var match   = new MatchEntity(groupId, DateTime.UtcNow.AddDays(1), "Quadra");

        match.SetLinkedPoll(pollId);

        Assert.Equal(pollId, match.LinkedPollId);
    }

    [Fact]
    public void SetLinkedPoll_WithNull_ClearsLinkedPollId()
    {
        var groupId = Guid.NewGuid();
        var match   = new MatchEntity(groupId, DateTime.UtcNow.AddDays(1), "Quadra");
        match.SetLinkedPoll(Guid.NewGuid()); // set first
        match.SetLinkedPoll(null);           // then clear

        Assert.Null(match.LinkedPollId);
    }

    [Fact]
    public void SetLinkedMatch_SetsLinkedMatchId()
    {
        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var poll    = CreatePoll(groupId);

        poll.SetLinkedMatch(matchId);

        Assert.Equal(matchId, poll.LinkedMatchId);
    }

    [Fact]
    public void SetLinkedMatch_WithNull_ClearsLinkedMatchId()
    {
        var groupId = Guid.NewGuid();
        var poll    = CreatePoll(groupId);
        poll.SetLinkedMatch(Guid.NewGuid()); // set first
        poll.SetLinkedMatch(null);            // then clear

        Assert.Null(poll.LinkedMatchId);
    }

    // ── Service projection tests ─────────────────────────────────────────────

    [Fact]
    public async Task GetUpcomingAsync_ReturnsLinkedPollId_WhenMatchHasLinkedPoll()
    {
        await using var db    = CreateDb();
        var service           = CreateService(db);
        var group             = await SeedGroupAsync(db);
        var pollId            = Guid.NewGuid();

        var match = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(1), "Quadra");
        match.SetLinkedPoll(pollId);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var result = await service.GetUpcomingAsync(group.Id, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Single(result.Data!);
        Assert.Equal(pollId, result.Data![0].LinkedPollId);
    }

    [Fact]
    public async Task GetUpcomingAsync_ReturnsNullLinkedPollId_WhenNoLinkExists()
    {
        await using var db = CreateDb();
        var service        = CreateService(db);
        var group          = await SeedGroupAsync(db);

        var match = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(1), "Quadra");
        // No SetLinkedPoll call — LinkedPollId stays null
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var result = await service.GetUpcomingAsync(group.Id, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Single(result.Data!);
        Assert.Null(result.Data![0].LinkedPollId);
    }

    [Fact]
    public async Task GetHeaderAsync_ReturnsLinkedPollId_WhenLinked()
    {
        await using var db = CreateDb();
        var service        = CreateService(db);
        var group          = await SeedGroupAsync(db);
        var pollId         = Guid.NewGuid();

        var match = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(1), "Quadra");
        match.SetLinkedPoll(pollId);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var result = await service.GetHeaderAsync(group.Id, match.Id, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(pollId, result.Data!.LinkedPollId);
    }

    [Fact]
    public async Task GetHeaderAsync_ReturnsNullLinkedPollId_WhenNotLinked()
    {
        await using var db = CreateDb();
        var service        = CreateService(db);
        var group          = await SeedGroupAsync(db);

        var match = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(1), "Quadra");
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var result = await service.GetHeaderAsync(group.Id, match.Id, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Null(result.Data!.LinkedPollId);
    }

    // ── Controller logic integration tests ───────────────────────────────────
    // These replicate the exact EF operations that MatchesController.SetLinkedPoll
    // performs so we can verify the persistence behaviour without spinning up
    // the full ASP.NET pipeline.

    [Fact]
    public async Task Link_SetsMatchLinkedPollId_AndPollLinkedMatchId()
    {
        await using var db = CreateDb();
        var group  = await SeedGroupAsync(db);

        var match = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(1), "Quadra");
        var poll  = CreatePoll(group.Id);
        db.Matches.Add(match);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        // ── Simulate controller action: link ──────────────────────────────
        var trackedMatch = await db.Matches.FirstAsync(m => m.Id == match.Id);
        var trackedPoll  = await db.Polls.FirstAsync(p => p.Id == poll.Id);

        // Clear old poll back-ref (none here, but run the same code path)
        if (trackedMatch.LinkedPollId.HasValue && trackedMatch.LinkedPollId != poll.Id)
        {
            var oldPoll = await db.Polls.FirstOrDefaultAsync(p => p.Id == trackedMatch.LinkedPollId.Value);
            oldPoll?.SetLinkedMatch(null);
        }

        // Handle case where new poll is already linked to another match
        if (trackedPoll.LinkedMatchId.HasValue && trackedPoll.LinkedMatchId != match.Id)
        {
            var oldMatch = await db.Matches.FirstOrDefaultAsync(m => m.Id == trackedPoll.LinkedMatchId.Value);
            oldMatch?.SetLinkedPoll(null);
        }

        trackedPoll.SetLinkedMatch(match.Id);
        trackedMatch.SetLinkedPoll(poll.Id);
        await db.SaveChangesAsync();

        // ── Assert ────────────────────────────────────────────────────────
        var persistedMatch = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        var persistedPoll  = await db.Polls.AsNoTracking().FirstAsync(p => p.Id == poll.Id);

        Assert.Equal(poll.Id,  persistedMatch.LinkedPollId);
        Assert.Equal(match.Id, persistedPoll.LinkedMatchId);
    }

    [Fact]
    public async Task Unlink_ClearsMatchLinkedPollId_AndPollLinkedMatchId()
    {
        await using var db = CreateDb();
        var group  = await SeedGroupAsync(db);

        var match = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(1), "Quadra");
        var poll  = CreatePoll(group.Id);
        db.Matches.Add(match);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        // Seed the existing link
        var trackedMatch = await db.Matches.FirstAsync(m => m.Id == match.Id);
        var trackedPoll  = await db.Polls.FirstAsync(p => p.Id == poll.Id);
        trackedMatch.SetLinkedPoll(poll.Id);
        trackedPoll.SetLinkedMatch(match.Id);
        await db.SaveChangesAsync();

        // ── Simulate controller action: unlink (PollId = null) ────────────
        var m2 = await db.Matches.FirstAsync(m => m.Id == match.Id);

        // Clear the old poll's back-reference
        if (m2.LinkedPollId.HasValue)
        {
            var oldPoll = await db.Polls.FirstOrDefaultAsync(p => p.Id == m2.LinkedPollId.Value);
            oldPoll?.SetLinkedMatch(null);
        }

        m2.SetLinkedPoll(null); // PollId is null → unlink
        await db.SaveChangesAsync();

        // ── Assert ────────────────────────────────────────────────────────
        var persistedMatch = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        var persistedPoll  = await db.Polls.AsNoTracking().FirstAsync(p => p.Id == poll.Id);

        Assert.Null(persistedMatch.LinkedPollId);
        Assert.Null(persistedPoll.LinkedMatchId);
    }

    [Fact]
    public async Task Link_WhenPollAlreadyLinkedToAnotherMatch_ClearsOldMatch()
    {
        await using var db = CreateDb();
        var group  = await SeedGroupAsync(db);

        // match1 is the original match linked to poll
        var match1 = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(1), "Quadra 1");
        var match2 = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(2), "Quadra 2");
        var poll   = CreatePoll(group.Id);
        db.Matches.Add(match1);
        db.Matches.Add(match2);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        // Seed: poll is currently linked to match1
        var tm1 = await db.Matches.FirstAsync(m => m.Id == match1.Id);
        var tp  = await db.Polls.FirstAsync(p => p.Id == poll.Id);
        tm1.SetLinkedPoll(poll.Id);
        tp.SetLinkedMatch(match1.Id);
        await db.SaveChangesAsync();

        // ── Simulate linking poll to match2 ──────────────────────────────
        var tm2        = await db.Matches.FirstAsync(m => m.Id == match2.Id);
        var newPoll    = await db.Polls.FirstAsync(p => p.Id == poll.Id);

        // Clear match2's old poll ref (none) — standard flow
        if (tm2.LinkedPollId.HasValue && tm2.LinkedPollId != poll.Id)
        {
            var oldPoll = await db.Polls.FirstOrDefaultAsync(p => p.Id == tm2.LinkedPollId.Value);
            oldPoll?.SetLinkedMatch(null);
        }

        // newPoll.LinkedMatchId == match1.Id which != match2.Id → clear match1
        if (newPoll.LinkedMatchId.HasValue && newPoll.LinkedMatchId != match2.Id)
        {
            var oldMatch = await db.Matches.FirstOrDefaultAsync(m => m.Id == newPoll.LinkedMatchId.Value);
            oldMatch?.SetLinkedPoll(null);
        }

        newPoll.SetLinkedMatch(match2.Id);
        tm2.SetLinkedPoll(poll.Id);
        await db.SaveChangesAsync();

        // ── Assert ────────────────────────────────────────────────────────
        var persistedMatch1 = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match1.Id);
        var persistedMatch2 = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match2.Id);
        var persistedPoll   = await db.Polls.AsNoTracking().FirstAsync(p => p.Id == poll.Id);

        Assert.Null(persistedMatch1.LinkedPollId);          // old match cleared
        Assert.Equal(poll.Id,   persistedMatch2.LinkedPollId); // new match linked
        Assert.Equal(match2.Id, persistedPoll.LinkedMatchId);  // poll points to new match
    }

    [Fact]
    public async Task Link_WhenMatchAlreadyHasADifferentPoll_ClearsOldPoll()
    {
        await using var db = CreateDb();
        var group  = await SeedGroupAsync(db);

        var match = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(1), "Quadra");
        var poll1 = CreatePoll(group.Id);
        var poll2 = CreatePoll(group.Id);
        db.Matches.Add(match);
        db.Polls.Add(poll1);
        db.Polls.Add(poll2);
        await db.SaveChangesAsync();

        // Seed: match is currently linked to poll1
        var tm  = await db.Matches.FirstAsync(m => m.Id == match.Id);
        var tp1 = await db.Polls.FirstAsync(p => p.Id == poll1.Id);
        tm.SetLinkedPoll(poll1.Id);
        tp1.SetLinkedMatch(match.Id);
        await db.SaveChangesAsync();

        // ── Simulate linking match to poll2 ───────────────────────────────
        var tm2  = await db.Matches.FirstAsync(m => m.Id == match.Id);
        var tp2  = await db.Polls.FirstAsync(p => p.Id == poll2.Id);
        var tp1r = await db.Polls.FirstAsync(p => p.Id == poll1.Id); // re-read in same context

        // tm2.LinkedPollId == poll1.Id which != poll2.Id → clear poll1's back-ref
        if (tm2.LinkedPollId.HasValue && tm2.LinkedPollId != poll2.Id)
        {
            var oldPoll = await db.Polls.FirstOrDefaultAsync(p => p.Id == tm2.LinkedPollId.Value);
            oldPoll?.SetLinkedMatch(null);
        }

        // poll2 has no existing link, so no old match to clear
        if (tp2.LinkedMatchId.HasValue && tp2.LinkedMatchId != match.Id)
        {
            var oldMatch = await db.Matches.FirstOrDefaultAsync(m => m.Id == tp2.LinkedMatchId.Value);
            oldMatch?.SetLinkedPoll(null);
        }

        tp2.SetLinkedMatch(match.Id);
        tm2.SetLinkedPoll(poll2.Id);
        await db.SaveChangesAsync();

        // ── Assert ────────────────────────────────────────────────────────
        var persistedMatch = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        var persistedPoll1 = await db.Polls.AsNoTracking().FirstAsync(p => p.Id == poll1.Id);
        var persistedPoll2 = await db.Polls.AsNoTracking().FirstAsync(p => p.Id == poll2.Id);

        Assert.Equal(poll2.Id,  persistedMatch.LinkedPollId);  // match now points to poll2
        Assert.Null(persistedPoll1.LinkedMatchId);               // old poll cleared
        Assert.Equal(match.Id, persistedPoll2.LinkedMatchId);   // new poll points to match
    }

    [Fact]
    public async Task Link_WhenMatchNotFound_MatchLookupReturnsNull()
    {
        await using var db = CreateDb();
        var group   = await SeedGroupAsync(db);
        var randomMatchId = Guid.NewGuid();

        // Simulate the controller's match lookup
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == randomMatchId && m.GroupId == group.Id);

        // A random ID that was never inserted should return null (maps to 404)
        Assert.Null(match);
    }

    [Fact]
    public async Task Link_WhenPollNotFound_DoesNotChangeMatch()
    {
        await using var db = CreateDb();
        var group  = await SeedGroupAsync(db);

        var match = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(1), "Quadra");
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var randomPollId = Guid.NewGuid();

        // Simulate the controller's poll lookup
        var newPoll = await db.Polls
            .FirstOrDefaultAsync(p => p.Id == randomPollId && p.GroupId == group.Id);

        // Poll not found → controller returns 400; match must not have been modified
        Assert.Null(newPoll);

        var persistedMatch = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == match.Id);
        Assert.Null(persistedMatch.LinkedPollId); // unchanged
    }
}
