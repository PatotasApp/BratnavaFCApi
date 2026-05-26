using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos.Polls;
using BratnavaFC.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

public class PollServiceTests
{
    // ── Factory helper ────────────────────────────────────────────────────────

    private static PollEntity MakePoll(
        Guid    groupId,
        string  status       = "open",
        DateOnly? deadlineDate = null,
        TimeOnly? deadlineTime = null)
    {
        var poll = new PollEntity(
            groupId, "Votação teste", null,
            allowMultipleVotes: false,
            showVotes: false,
            createdByUserId: Guid.NewGuid(),
            deadlineDate: deadlineDate,
            deadlineTime: deadlineTime);

        if (status == "closed") poll.Close();
        return poll;
    }

    // ── UpdateDeadlineAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateDeadlineAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db  = DbContextFactory.Create(nameof(UpdateDeadlineAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = new PollService(db, Mock.Of<IPushService>(), Mock.Of<INotificationScheduler>(), Mock.Of<ILogger<PollService>>());

        // Act
        var result = await sut.UpdateDeadlineAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            new UpdatePollDeadlineDto(),
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("não encontrada");
    }

    [Fact]
    public async Task UpdateDeadlineAsync_WhenGroupIdMismatch_ShouldReturnFailure()
    {
        // Arrange — poll existe mas pertence a outro grupo
        await using var db  = DbContextFactory.Create(nameof(UpdateDeadlineAsync_WhenGroupIdMismatch_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut = new PollService(db, Mock.Of<IPushService>(), Mock.Of<INotificationScheduler>(), Mock.Of<ILogger<PollService>>());
        var dto = new UpdatePollDeadlineDto { DeadlineDate = "2030-01-01" };

        // Act — groupId diferente do poll
        var result = await sut.UpdateDeadlineAsync(
            Guid.NewGuid(), poll.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("não encontrada");
    }

    [Fact]
    public async Task UpdateDeadlineAsync_WhenOpen_ShouldSetDeadlineDateAndTime()
    {
        // Arrange
        await using var db  = DbContextFactory.Create(nameof(UpdateDeadlineAsync_WhenOpen_ShouldSetDeadlineDateAndTime));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut = new PollService(db, Mock.Of<IPushService>(), Mock.Of<INotificationScheduler>(), Mock.Of<ILogger<PollService>>());
        var dto = new UpdatePollDeadlineDto { DeadlineDate = "2030-12-31", DeadlineTime = "20:00" };

        // Act
        var result = await sut.UpdateDeadlineAsync(groupId, poll.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var reloaded = await db.Polls.FirstAsync(p => p.Id == poll.Id);
        reloaded.DeadlineDate.Should().Be(new DateOnly(2030, 12, 31));
        reloaded.DeadlineTime.Should().Be(new TimeOnly(20, 0));
        reloaded.Status.Should().Be("open", "poll aberta não deve mudar de status.");
    }

    [Fact]
    public async Task UpdateDeadlineAsync_WhenOpen_DateOnly_ShouldSetDateAndClearExistingTime()
    {
        // Arrange — poll já tem horário definido; novo prazo não tem horário → limpar
        await using var db  = DbContextFactory.Create(nameof(UpdateDeadlineAsync_WhenOpen_DateOnly_ShouldSetDateAndClearExistingTime));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId,
            deadlineDate: new DateOnly(2025, 1, 1),
            deadlineTime: new TimeOnly(18, 0));
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut = new PollService(db, Mock.Of<IPushService>(), Mock.Of<INotificationScheduler>(), Mock.Of<ILogger<PollService>>());
        var dto = new UpdatePollDeadlineDto { DeadlineDate = "2030-06-15" }; // sem horário

        // Act
        var result = await sut.UpdateDeadlineAsync(groupId, poll.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var reloaded = await db.Polls.FirstAsync(p => p.Id == poll.Id);
        reloaded.DeadlineDate.Should().Be(new DateOnly(2030, 6, 15));
        reloaded.DeadlineTime.Should().BeNull("sem horário informado, o horário anterior deve ser limpo.");
    }

    [Fact]
    public async Task UpdateDeadlineAsync_WhenClearDeadline_ShouldRemoveBothFields()
    {
        // Arrange
        await using var db  = DbContextFactory.Create(nameof(UpdateDeadlineAsync_WhenClearDeadline_ShouldRemoveBothFields));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId,
            deadlineDate: new DateOnly(2030, 1, 1),
            deadlineTime: new TimeOnly(12, 0));
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut = new PollService(db, Mock.Of<IPushService>(), Mock.Of<INotificationScheduler>(), Mock.Of<ILogger<PollService>>());
        var dto = new UpdatePollDeadlineDto { ClearDeadline = true };

        // Act
        var result = await sut.UpdateDeadlineAsync(groupId, poll.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var reloaded = await db.Polls.FirstAsync(p => p.Id == poll.Id);
        reloaded.DeadlineDate.Should().BeNull();
        reloaded.DeadlineTime.Should().BeNull();
        reloaded.Status.Should().Be("open", "limpar prazo de poll aberta não altera o status.");
    }

    [Fact]
    public async Task UpdateDeadlineAsync_WhenClosed_ShouldReopenAndSetDeadline()
    {
        // Arrange — poll encerrada manualmente
        await using var db  = DbContextFactory.Create(nameof(UpdateDeadlineAsync_WhenClosed_ShouldReopenAndSetDeadline));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId, status: "closed");
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut = new PollService(db, Mock.Of<IPushService>(), Mock.Of<INotificationScheduler>(), Mock.Of<ILogger<PollService>>());
        var dto = new UpdatePollDeadlineDto { DeadlineDate = "2030-12-31", DeadlineTime = "18:00" };

        // Act
        var result = await sut.UpdateDeadlineAsync(groupId, poll.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var reloaded = await db.Polls.FirstAsync(p => p.Id == poll.Id);
        reloaded.Status.Should().Be("open",
            "estender o prazo de uma votação encerrada deve reabri-la automaticamente.");
        reloaded.DeadlineDate.Should().Be(new DateOnly(2030, 12, 31));
        reloaded.DeadlineTime.Should().Be(new TimeOnly(18, 0));
    }

    [Fact]
    public async Task UpdateDeadlineAsync_WhenClosed_AndClearDeadline_ShouldReopenAndRemoveDeadline()
    {
        // Arrange — poll fechada que também tinha prazo expirado
        await using var db  = DbContextFactory.Create(nameof(UpdateDeadlineAsync_WhenClosed_AndClearDeadline_ShouldReopenAndRemoveDeadline));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId, status: "closed",
            deadlineDate: new DateOnly(2025, 6, 1));
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut = new PollService(db, Mock.Of<IPushService>(), Mock.Of<INotificationScheduler>(), Mock.Of<ILogger<PollService>>());
        var dto = new UpdatePollDeadlineDto { ClearDeadline = true };

        // Act
        var result = await sut.UpdateDeadlineAsync(groupId, poll.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var reloaded = await db.Polls.FirstAsync(p => p.Id == poll.Id);
        reloaded.Status.Should().Be("open",
            "remover o prazo de uma votação encerrada também deve reabri-la.");
        reloaded.DeadlineDate.Should().BeNull();
        reloaded.DeadlineTime.Should().BeNull();
    }

    [Fact]
    public async Task UpdateDeadlineAsync_WhenClosed_ShouldPersistBothChangesInOneTransaction()
    {
        // Garante que status "open" + novo deadline são salvos juntos (não em saves separados)
        await using var db  = DbContextFactory.Create(nameof(UpdateDeadlineAsync_WhenClosed_ShouldPersistBothChangesInOneTransaction));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId, status: "closed");
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var sut = new PollService(db, Mock.Of<IPushService>(), Mock.Of<INotificationScheduler>(), Mock.Of<ILogger<PollService>>());
        var dto = new UpdatePollDeadlineDto { DeadlineDate = "2031-03-10", DeadlineTime = "09:30" };

        await sut.UpdateDeadlineAsync(groupId, poll.Id, dto, CancellationToken.None);

        // Recarrega via novo contexto para garantir persistência real
        await using var db2 = DbContextFactory.Create(nameof(UpdateDeadlineAsync_WhenClosed_ShouldPersistBothChangesInOneTransaction));
        var persisted = await db2.Polls.AsNoTracking().FirstAsync(p => p.Id == poll.Id);
        persisted.Status.Should().Be("open");
        persisted.DeadlineDate.Should().Be(new DateOnly(2031, 3, 10));
        persisted.DeadlineTime.Should().Be(new TimeOnly(9, 30));
    }
}
