using BratnavaFC.Domain.Entities;
using FluentAssertions;

namespace BranavaFC.Tests;

public class PollEntityTests
{
    private static PollEntity MakePoll(
        bool allowMultiple = false,
        bool showVotes = false,
        string status = "open",
        DateOnly? deadlineDate = null,
        TimeOnly? deadlineTime = null,
        string type = "poll")
    {
        var poll = new PollEntity(
            Guid.NewGuid(), "Título teste", null,
            allowMultiple, showVotes, Guid.NewGuid(),
            deadlineDate, deadlineTime, type);

        if (status == "closed") poll.Close();
        return poll;
    }

    // ── Construtor ────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_HappyPath_ShouldSetProperties()
    {
        var groupId = Guid.NewGuid();
        var poll = new PollEntity(groupId, "Qual time?", "Descrição", true, false, null);

        poll.GroupId.Should().Be(groupId);
        poll.Title.Should().Be("Qual time?");
        poll.Description.Should().Be("Descrição");
        poll.AllowMultipleVotes.Should().BeTrue();
        poll.Status.Should().Be("open");
        poll.Type.Should().Be("poll");
    }

    [Fact]
    public void Constructor_TrimTitle_ShouldStripWhitespace()
    {
        var poll = new PollEntity(Guid.NewGuid(), "  Título  ", null, false, false, null);

        poll.Title.Should().Be("Título");
    }

    [Fact]
    public void Constructor_EmptyGroupId_ShouldThrow()
    {
        var act = () => new PollEntity(Guid.Empty, "Título", null, false, false, null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_EmptyTitle_ShouldThrow()
    {
        var act = () => new PollEntity(Guid.NewGuid(), "  ", null, false, false, null);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── Close / Reopen ────────────────────────────────────────────────────────

    [Fact]
    public void Close_ShouldSetStatusClosed()
    {
        var poll = MakePoll();

        poll.Close();

        poll.Status.Should().Be("closed");
    }

    [Fact]
    public void Reopen_ShouldSetStatusOpen()
    {
        var poll = MakePoll(status: "closed");

        poll.Reopen();

        poll.Status.Should().Be("open");
    }

    // ── IsEventType ───────────────────────────────────────────────────────────

    [Fact]
    public void IsEventType_WhenTypePoll_ShouldReturnFalse()
    {
        var poll = MakePoll(type: "poll");

        poll.IsEventType().Should().BeFalse();
    }

    [Fact]
    public void IsEventType_WhenTypeEvent_ShouldReturnTrue()
    {
        var poll = MakePoll(type: "event");

        poll.IsEventType().Should().BeTrue();
    }

    // ── HasExpiredDeadline ────────────────────────────────────────────────────

    [Fact]
    public void HasExpiredDeadline_WhenNoDeadline_ShouldReturnFalse()
    {
        var poll = MakePoll();

        poll.HasExpiredDeadline().Should().BeFalse();
    }

    [Fact]
    public void HasExpiredDeadline_WhenDeadlineInFuture_ShouldReturnFalse()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var poll = MakePoll(deadlineDate: future);

        poll.HasExpiredDeadline().Should().BeFalse();
    }

    [Fact]
    public void HasExpiredDeadline_WhenDeadlineInPast_ShouldReturnTrue()
    {
        var past = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        var poll = MakePoll(deadlineDate: past);

        poll.HasExpiredDeadline().Should().BeTrue();
    }

    // ── ValidateVoteChange ────────────────────────────────────────────────────

    [Fact]
    public void ValidateVoteChange_WhenOpen_ShouldReturnNull()
    {
        var poll = MakePoll();

        var error = poll.ValidateVoteChange();

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateVoteChange_WhenClosed_ShouldReturnError()
    {
        var poll = MakePoll(status: "closed");

        var error = poll.ValidateVoteChange();

        error.Should().NotBeNullOrEmpty();
        error.Should().Contain("encerrada");
    }

    [Fact]
    public void ValidateVoteChange_WhenDeadlineExpired_ShouldReturnError()
    {
        var past = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        var poll = MakePoll(deadlineDate: past);

        var error = poll.ValidateVoteChange();

        error.Should().NotBeNullOrEmpty();
        error.Should().Contain("prazo");
    }

    // ── ValidateVote ──────────────────────────────────────────────────────────

    [Fact]
    public void ValidateVote_WhenOpenAndSingleOption_ShouldReturnNull()
    {
        var poll = MakePoll(allowMultiple: false);

        var error = poll.ValidateVote(1);

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateVote_WhenSingleVotePollAndMultipleOptions_ShouldReturnError()
    {
        var poll = MakePoll(allowMultiple: false);

        var error = poll.ValidateVote(2);

        error.Should().NotBeNullOrEmpty();
        error.Should().Contain("uma opção");
    }

    [Fact]
    public void ValidateVote_WhenMultipleVotesAllowed_ShouldAcceptMultipleOptions()
    {
        var poll = MakePoll(allowMultiple: true);

        var error = poll.ValidateVote(3);

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateVote_WhenClosed_ShouldFailBeforeCheckingOptionCount()
    {
        var poll = MakePoll(status: "closed", allowMultiple: true);

        var error = poll.ValidateVote(1);

        error.Should().Contain("encerrada");
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_PartialUpdate_ShouldOnlyChangeProvidedFields()
    {
        var poll = MakePoll();
        var originalDescription = poll.Description;

        poll.Update("Novo título", null, null, null, null, null);

        poll.Title.Should().Be("Novo título");
        poll.Description.Should().Be(originalDescription);
    }

    [Fact]
    public void Update_ClearDeadline_ShouldRemoveDeadline()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var poll = MakePoll(deadlineDate: future);

        poll.Update(null, null, null, null, null, null, clearDeadline: true);

        poll.DeadlineDate.Should().BeNull();
        poll.DeadlineTime.Should().BeNull();
    }

    [Fact]
    public void Update_SetDeadline_ShouldUpdateDeadline()
    {
        var poll = MakePoll();
        var newDeadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));

        poll.Update(null, null, null, null, newDeadline, null);

        poll.DeadlineDate.Should().Be(newDeadline);
    }

    [Fact]
    public void Update_AllowMultipleVotes_ShouldToggle()
    {
        var poll = MakePoll(allowMultiple: false);

        poll.Update(null, null, true, null, null, null);

        poll.AllowMultipleVotes.Should().BeTrue();
    }

    // ── SetDeadline ───────────────────────────────────────────────────────────

    [Fact]
    public void SetDeadline_ShouldSetDateAndTime()
    {
        var poll = MakePoll();
        var date = new DateOnly(2030, 8, 15);
        var time = new TimeOnly(21, 0);

        poll.SetDeadline(date, time);

        poll.DeadlineDate.Should().Be(date);
        poll.DeadlineTime.Should().Be(time);
    }

    [Fact]
    public void SetDeadline_DateOnly_ShouldSetDateAndLeaveTimeNull()
    {
        var poll = MakePoll();

        poll.SetDeadline(new DateOnly(2030, 1, 1), null);

        poll.DeadlineDate.Should().Be(new DateOnly(2030, 1, 1));
        poll.DeadlineTime.Should().BeNull();
    }

    [Fact]
    public void SetDeadline_WithNullDate_ShouldClearBothFields()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var poll = MakePoll(deadlineDate: future, deadlineTime: new TimeOnly(12, 0));

        poll.SetDeadline(null, null);

        poll.DeadlineDate.Should().BeNull();
        poll.DeadlineTime.Should().BeNull();
    }

    [Fact]
    public void SetDeadline_ShouldReplaceExistingDeadline()
    {
        var old = new DateOnly(2025, 1, 1);
        var poll = MakePoll(deadlineDate: old, deadlineTime: new TimeOnly(18, 0));
        var newDate = new DateOnly(2030, 6, 30);
        var newTime = new TimeOnly(23, 59);

        poll.SetDeadline(newDate, newTime);

        poll.DeadlineDate.Should().Be(newDate);
        poll.DeadlineTime.Should().Be(newTime);
    }

    [Fact]
    public void SetDeadline_ShouldNotAffectOtherFields()
    {
        var poll = MakePoll(allowMultiple: true, showVotes: true, status: "open");

        poll.SetDeadline(new DateOnly(2030, 3, 1), null);

        poll.Title.Should().Be("Título teste");
        poll.AllowMultipleVotes.Should().BeTrue();
        poll.ShowVotes.Should().BeTrue();
        poll.Status.Should().Be("open");
    }
}

// ── PollOptionEntity ──────────────────────────────────────────────────────────

public class PollOptionEntityTests
{
    [Fact]
    public void Constructor_HappyPath_ShouldSetProperties()
    {
        var pollId = Guid.NewGuid();
        var option = new PollOptionEntity(pollId, "Opção A", "Desc", null);

        option.PollId.Should().Be(pollId);
        option.Text.Should().Be("Opção A");
        option.Description.Should().Be("Desc");
    }

    [Fact]
    public void Constructor_EmptyPollId_ShouldThrow()
    {
        var act = () => new PollOptionEntity(Guid.Empty, "Opção A", null, null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_EmptyText_ShouldThrow()
    {
        var act = () => new PollOptionEntity(Guid.NewGuid(), "  ", null, null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Update_ShouldChangeBothFields()
    {
        var option = new PollOptionEntity(Guid.NewGuid(), "Original", "Desc original", null);

        option.Update("Novo texto", "Nova descrição");

        option.Text.Should().Be("Novo texto");
        option.Description.Should().Be("Nova descrição");
    }

    [Fact]
    public void Update_NullArgs_ShouldNotChangeFields()
    {
        var option = new PollOptionEntity(Guid.NewGuid(), "Original", "Desc", null);

        option.Update(null, null);

        option.Text.Should().Be("Original");
        option.Description.Should().Be("Desc");
    }
}
