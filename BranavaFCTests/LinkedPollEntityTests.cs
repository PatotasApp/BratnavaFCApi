using BratnavaFC.Domain.Entities;
using FluentAssertions;

namespace BranavaFC.Tests;

/// <summary>
/// Unit tests for the SetLinkedPoll / SetLinkedMatch domain methods
/// added to MatchEntity and PollEntity.
/// No infrastructure dependencies — pure in-memory entity logic.
/// </summary>
public sealed class LinkedPollEntityTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MatchEntity MakeMatch() =>
        new(Guid.NewGuid(), DateTime.UtcNow, "Arena");

    private static PollEntity MakePoll(Guid? groupId = null, string type = "poll") =>
        new(groupId ?? Guid.NewGuid(), "Votação", null, false, false, null, type: type);

    // ── MatchEntity.SetLinkedPoll ─────────────────────────────────────────────

    [Fact]
    public void MatchEntity_SetLinkedPoll_ShouldPersistPollId()
    {
        var match  = MakeMatch();
        var pollId = Guid.NewGuid();

        match.SetLinkedPoll(pollId);

        match.LinkedPollId.Should().Be(pollId);
    }

    [Fact]
    public void MatchEntity_SetLinkedPoll_WithNull_ShouldClearPollId()
    {
        var match = MakeMatch();
        match.SetLinkedPoll(Guid.NewGuid());

        match.SetLinkedPoll(null);

        match.LinkedPollId.Should().BeNull();
    }

    [Fact]
    public void MatchEntity_SetLinkedPoll_ShouldBumpUpdateDate()
    {
        var match  = MakeMatch();
        var before = match.UpdateDate ?? DateTime.MinValue;

        match.SetLinkedPoll(Guid.NewGuid());

        match.UpdateDate.Should().NotBeNull();
        match.UpdateDate!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void MatchEntity_SetLinkedPoll_ShouldNotTouchOtherFields()
    {
        var groupId = Guid.NewGuid();
        var match   = new MatchEntity(groupId, DateTime.UtcNow, "Arena");

        match.SetLinkedPoll(Guid.NewGuid());

        match.GroupId.Should().Be(groupId);
        match.PlaceName.Should().Be("Arena");
    }

    [Fact]
    public void MatchEntity_SetLinkedPoll_CalledTwice_ShouldUseLatestValue()
    {
        var match  = MakeMatch();
        match.SetLinkedPoll(Guid.NewGuid());
        var second = Guid.NewGuid();

        match.SetLinkedPoll(second);

        match.LinkedPollId.Should().Be(second);
    }

    [Fact]
    public void MatchEntity_SetLinkedPoll_StartsAsNull()
    {
        var match = MakeMatch();

        match.LinkedPollId.Should().BeNull();
    }

    // ── PollEntity.SetLinkedMatch ─────────────────────────────────────────────

    [Fact]
    public void PollEntity_SetLinkedMatch_ShouldPersistMatchId()
    {
        var poll    = MakePoll();
        var matchId = Guid.NewGuid();

        poll.SetLinkedMatch(matchId);

        poll.LinkedMatchId.Should().Be(matchId);
    }

    [Fact]
    public void PollEntity_SetLinkedMatch_WithNull_ShouldClearMatchId()
    {
        var poll = MakePoll();
        poll.SetLinkedMatch(Guid.NewGuid());

        poll.SetLinkedMatch(null);

        poll.LinkedMatchId.Should().BeNull();
    }

    [Fact]
    public void PollEntity_SetLinkedMatch_ShouldBumpUpdateDate()
    {
        var poll   = MakePoll();
        var before = poll.UpdateDate ?? DateTime.MinValue;

        poll.SetLinkedMatch(Guid.NewGuid());

        poll.UpdateDate.Should().NotBeNull();
        poll.UpdateDate!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void PollEntity_SetLinkedMatch_ShouldNotTouchOtherFields()
    {
        var groupId = Guid.NewGuid();
        var poll    = new PollEntity(groupId, "Título", "Desc", true, false, null);

        poll.SetLinkedMatch(Guid.NewGuid());

        poll.GroupId.Should().Be(groupId);
        poll.Title.Should().Be("Título");
        poll.Description.Should().Be("Desc");
        poll.AllowMultipleVotes.Should().BeTrue();
        poll.Status.Should().Be("open");
    }

    [Fact]
    public void PollEntity_SetLinkedMatch_CalledTwice_ShouldUseLatestValue()
    {
        var poll   = MakePoll();
        poll.SetLinkedMatch(Guid.NewGuid());
        var second = Guid.NewGuid();

        poll.SetLinkedMatch(second);

        poll.LinkedMatchId.Should().Be(second);
    }

    [Fact]
    public void PollEntity_SetLinkedMatch_StartsAsNull()
    {
        var poll = MakePoll();

        poll.LinkedMatchId.Should().BeNull();
    }

    [Fact]
    public void PollEntity_SetLinkedMatch_WorksForEventType()
    {
        var poll    = MakePoll(type: "event");
        var matchId = Guid.NewGuid();

        poll.SetLinkedMatch(matchId);

        poll.LinkedMatchId.Should().Be(matchId);
        poll.IsEventType().Should().BeTrue("type must remain 'event' after SetLinkedMatch");
    }

    [Fact]
    public void PollEntity_SetLinkedMatch_SetThenClear_MatchStillOpen()
    {
        // Linking/unlinking a match must never change the poll's open/closed status
        var poll = MakePoll();

        poll.SetLinkedMatch(Guid.NewGuid());
        poll.SetLinkedMatch(null);

        poll.Status.Should().Be("open");
    }
}
