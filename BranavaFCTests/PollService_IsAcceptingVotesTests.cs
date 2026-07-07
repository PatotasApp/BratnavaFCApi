using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>
/// Testes para PollService.ComputeIsAcceptingVotes() e a exposição
/// do campo IsAcceptingVotes no PollDto.
/// A lógica deve ser calculada pelo servidor — o frontend não deve computar deadlines.
/// </summary>
public class PollService_IsAcceptingVotesTests
{
    // ── helper ───────────────────────────────────────────────────────────────

    private static PollService BuildSut(AppDbContext db)
        => new(db,
               Mock.Of<IPushService>(),
               Mock.Of<INotificationScheduler>(),
               Mock.Of<ILogger<PollService>>());

    private static PollEntity MakePoll(
        Guid      groupId,
        string    status       = "open",
        DateOnly? deadlineDate = null,
        TimeOnly? deadlineTime = null)
    {
        var poll = new PollEntity(
            groupId, "Título teste", null,
            allowMultipleVotes: false,
            showVotes: false,
            createdByUserId: Guid.NewGuid(),
            deadlineDate: deadlineDate,
            deadlineTime: deadlineTime);

        if (status == "closed") poll.Close();
        return poll;
    }

    // ── ComputeIsAcceptingVotes (estático / unitário) ─────────────────────────

    [Fact]
    public void ComputeIsAcceptingVotes_WhenOpen_NoDeadline_ShouldReturnTrue()
    {
        var result = PollService.ComputeIsAcceptingVotes("open", null, null);
        result.Should().BeTrue("poll aberta sem prazo deve aceitar votos indefinidamente.");
    }

    [Fact]
    public void ComputeIsAcceptingVotes_WhenOpen_FutureDeadline_ShouldReturnTrue()
    {
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var result = PollService.ComputeIsAcceptingVotes("open", futureDate, null);
        result.Should().BeTrue("prazo futuro — ainda deve aceitar votos.");
    }

    [Fact]
    public void ComputeIsAcceptingVotes_WhenOpen_PastDeadline_ShouldReturnFalse()
    {
        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var result = PollService.ComputeIsAcceptingVotes("open", pastDate, null);
        result.Should().BeFalse("prazo vencido — não deve aceitar votos mesmo com status 'open'.");
    }

    [Fact]
    public void ComputeIsAcceptingVotes_WhenClosed_NoDeadline_ShouldReturnFalse()
    {
        var result = PollService.ComputeIsAcceptingVotes("closed", null, null);
        result.Should().BeFalse("poll encerrada nunca deve aceitar votos.");
    }

    [Fact]
    public void ComputeIsAcceptingVotes_WhenClosed_FutureDeadline_ShouldReturnFalse()
    {
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var result = PollService.ComputeIsAcceptingVotes("closed", futureDate, null);
        result.Should().BeFalse("status 'closed' prevalece sobre prazo futuro.");
    }

    [Fact]
    public void ComputeIsAcceptingVotes_WhenOpen_DeadlineTodayWithFutureTime_ShouldReturnTrue()
    {
        // Usa data e hora do MESMO instante futuro para não quebrar na virada do dia UTC
        var futureInstant = DateTime.UtcNow.AddHours(2);
        var result = PollService.ComputeIsAcceptingVotes(
            "open", DateOnly.FromDateTime(futureInstant), TimeOnly.FromDateTime(futureInstant));
        result.Should().BeTrue("o prazo ainda não chegou.");
    }

    [Fact]
    public void ComputeIsAcceptingVotes_WhenOpen_DeadlineTodayWithPastTime_ShouldReturnFalse()
    {
        // Usa data e hora do MESMO instante passado para não quebrar na virada do dia UTC
        var pastInstant = DateTime.UtcNow.AddHours(-2);
        var result = PollService.ComputeIsAcceptingVotes(
            "open", DateOnly.FromDateTime(pastInstant), TimeOnly.FromDateTime(pastInstant));
        result.Should().BeFalse("o prazo já passou.");
    }

    [Fact]
    public void ComputeIsAcceptingVotes_WhenOpen_DateOnlyNoTime_UsesEndOfDay()
    {
        // Sem horário: usa TimeOnly.MaxValue (23:59:59.999)
        // Logo, uma data futura sem horário ainda deve aceitar votos
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var result   = PollService.ComputeIsAcceptingVotes("open", tomorrow, null);
        result.Should().BeTrue("data futura sem horário usa fim do dia — deve aceitar.");
    }

    // ── Integração: IsAcceptingVotes exposto no PollDto ──────────────────────

    [Fact]
    public async Task GetPollAsync_WhenOpen_NoDeadline_ShouldReturnIsAcceptingVotesTrue()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollAsync_WhenOpen_NoDeadline_ShouldReturnIsAcceptingVotesTrue));
        var groupId = Guid.NewGuid();
        var poll    = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut    = BuildSut(db);
        var result = await sut.GetPollAsync(groupId, poll.Id, Guid.Empty, isAdmin: false);

        result.Success.Should().BeTrue();
        result.Data!.IsAcceptingVotes.Should().BeTrue();
    }

    [Fact]
    public async Task GetPollAsync_WhenClosed_ShouldReturnIsAcceptingVotesFalse()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollAsync_WhenClosed_ShouldReturnIsAcceptingVotesFalse));
        var groupId = Guid.NewGuid();
        var poll    = MakePoll(groupId, status: "closed");
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut    = BuildSut(db);
        var result = await sut.GetPollAsync(groupId, poll.Id, Guid.Empty, isAdmin: false);

        result.Success.Should().BeTrue();
        result.Data!.IsAcceptingVotes.Should().BeFalse();
    }

    [Fact]
    public async Task GetPollAsync_WhenOpen_PastDeadline_ShouldReturnIsAcceptingVotesFalse()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollAsync_WhenOpen_PastDeadline_ShouldReturnIsAcceptingVotesFalse));
        var groupId = Guid.NewGuid();
        var poll    = MakePoll(groupId,
            deadlineDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut    = BuildSut(db);
        var result = await sut.GetPollAsync(groupId, poll.Id, Guid.Empty, isAdmin: false);

        result.Success.Should().BeTrue();
        result.Data!.IsAcceptingVotes.Should().BeFalse(
            "mesmo com status 'open', o prazo já venceu — backend deve retornar false.");
    }
}
