using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos.Polls;
using BratnavaFC.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Testes para SetAllowGuestsAsync, AddGuestAsync e RemoveGuestAsync do PollService.
/// </summary>
public class PollGuestServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PollService BuildSut(BratnavaFC.Infrastructure.Data.AppDbContext db)
        => new(db,
               Mock.Of<IPushService>(),
               Mock.Of<INotificationScheduler>(),
               Mock.Of<ILogger<PollService>>());

    private static PollEntity MakeEventPoll(
        Guid    groupId,
        bool    allowGuests  = true,
        string  status       = "open",
        DateOnly? deadlineDate = null)
    {
        var poll = new PollEntity(
            groupId, "Pelada do mês", null,
            allowMultipleVotes: false,
            showVotes: true,
            createdByUserId: Guid.NewGuid(),
            deadlineDate: deadlineDate,
            type: "event",
            allowGuests: allowGuests);

        if (status == "closed") poll.Close();
        return poll;
    }

    private static PlayerEntity MakePlayer(Guid groupId, string name = "Jogador")
        => new(name, Guid.NewGuid(), groupId, 0m, false);

    /// <summary>Seeds a "Sim" option as the first (sortOrder 0) option of the poll.</summary>
    private static PollOptionEntity MakeSimOption(Guid pollId)
        => new(pollId, "Sim", null, null, sortOrder: 0);

    // ── SetAllowGuestsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task SetAllowGuestsAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(SetAllowGuestsAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.SetAllowGuestsAsync(Guid.NewGuid(), Guid.NewGuid(), true);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("não encontrada");
    }

    [Fact]
    public async Task SetAllowGuestsAsync_WhenPollTypeIsPoll_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(SetAllowGuestsAsync_WhenPollTypeIsPoll_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = new PollEntity(groupId, "Votação simples", null, false, false, null, type: "poll");
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.SetAllowGuestsAsync(groupId, poll.Id, true);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("eventos");
    }

    [Fact]
    public async Task SetAllowGuestsAsync_EnableOnEvent_ShouldPersistTrue()
    {
        await using var db = DbContextFactory.Create(nameof(SetAllowGuestsAsync_EnableOnEvent_ShouldPersistTrue));
        var groupId = Guid.NewGuid();
        var poll = MakeEventPoll(groupId, allowGuests: false);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.SetAllowGuestsAsync(groupId, poll.Id, true);

        result.Success.Should().BeTrue();
        var reloaded = await db.Polls.FirstAsync(p => p.Id == poll.Id);
        reloaded.AllowGuests.Should().BeTrue();
    }

    [Fact]
    public async Task SetAllowGuestsAsync_DisableOnEvent_ShouldPersistFalse()
    {
        await using var db = DbContextFactory.Create(nameof(SetAllowGuestsAsync_DisableOnEvent_ShouldPersistFalse));
        var groupId = Guid.NewGuid();
        var poll = MakeEventPoll(groupId, allowGuests: true);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.SetAllowGuestsAsync(groupId, poll.Id, false);

        result.Success.Should().BeTrue();
        var reloaded = await db.Polls.FirstAsync(p => p.Id == poll.Id);
        reloaded.AllowGuests.Should().BeFalse();
    }

    [Fact]
    public async Task SetAllowGuestsAsync_WhenGroupIdMismatch_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(SetAllowGuestsAsync_WhenGroupIdMismatch_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakeEventPoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.SetAllowGuestsAsync(Guid.NewGuid(), poll.Id, true);

        result.Success.Should().BeFalse();
    }

    // ── AddGuestAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddGuestAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);
        var dto = new AddPollGuestDto { GuestName = "João", IsAdult = true };

        var result = await sut.AddGuestAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), dto);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("não encontrado");
    }

    [Fact]
    public async Task AddGuestAsync_WhenAllowGuestsFalse_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestAsync_WhenAllowGuestsFalse_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakeEventPoll(groupId, allowGuests: false);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);
        var dto = new AddPollGuestDto { GuestName = "Maria", IsAdult = true };

        var result = await sut.AddGuestAsync(groupId, poll.Id, Guid.NewGuid(), dto);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("não permite convidados");
    }

    [Fact]
    public async Task AddGuestAsync_WhenPollClosed_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestAsync_WhenPollClosed_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakeEventPoll(groupId, allowGuests: true, status: "closed");
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);
        var dto = new AddPollGuestDto { GuestName = "Pedro", IsAdult = false };

        var result = await sut.AddGuestAsync(groupId, poll.Id, Guid.NewGuid(), dto);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("encerrado");
    }

    [Fact]
    public async Task AddGuestAsync_WhenDeadlineExpired_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestAsync_WhenDeadlineExpired_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var past = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        var poll = MakeEventPoll(groupId, allowGuests: true, deadlineDate: past);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);
        var dto = new AddPollGuestDto { GuestName = "Ana", IsAdult = true };

        var result = await sut.AddGuestAsync(groupId, poll.Id, Guid.NewGuid(), dto);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("prazo");
    }

    [Fact]
    public async Task AddGuestAsync_WhenVoterHasNoGoingVote_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestAsync_WhenVoterHasNoGoingVote_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var player  = MakePlayer(groupId);
        var poll    = MakeEventPoll(groupId, allowGuests: true);
        var simOpt  = MakeSimOption(poll.Id);
        db.Players.Add(player);
        db.Polls.Add(poll);
        db.PollOptions.Add(simOpt);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);
        var dto = new AddPollGuestDto { GuestName = "Visitante", IsAdult = true };

        // Player exists but has no vote registered
        var result = await sut.AddGuestAsync(groupId, poll.Id, player.Id, dto);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("confirmar presença");
    }

    [Fact]
    public async Task AddGuestAsync_WhenVoterHasGoingVote_ShouldPersistGuest()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestAsync_WhenVoterHasGoingVote_ShouldPersistGuest));
        var groupId = Guid.NewGuid();
        var player  = MakePlayer(groupId, "Fulano");
        var poll    = MakeEventPoll(groupId, allowGuests: true);
        var simOpt  = MakeSimOption(poll.Id);
        db.Players.Add(player);
        db.Polls.Add(poll);
        db.PollOptions.Add(simOpt);
        await db.SaveChangesAsync();

        // Cast a "going" vote for the player
        db.PollVotes.Add(new PollVoteEntity(poll.Id, simOpt.Id, player.Id));
        await db.SaveChangesAsync();

        var sut = BuildSut(db);
        var dto = new AddPollGuestDto { GuestName = "Convidado X", IsAdult = false };

        var result = await sut.AddGuestAsync(groupId, poll.Id, player.Id, dto);

        result.Success.Should().BeTrue();
        result.Data!.GuestName.Should().Be("Convidado X");
        result.Data!.IsAdult.Should().BeFalse();
        result.Data!.VoterPlayerId.Should().Be(player.Id);

        var persisted = await db.PollGuests.FirstOrDefaultAsync(g => g.PollId == poll.Id);
        persisted.Should().NotBeNull();
        persisted!.GuestName.Should().Be("Convidado X");
    }

    [Fact]
    public async Task AddGuestAsync_WhenGuestNameEmpty_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(AddGuestAsync_WhenGuestNameEmpty_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakeEventPoll(groupId, allowGuests: true);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);
        var dto = new AddPollGuestDto { GuestName = "   ", IsAdult = true };

        var result = await sut.AddGuestAsync(groupId, poll.Id, Guid.NewGuid(), dto);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("obrigatório");
    }

    // ── RemoveGuestAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveGuestAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveGuestAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.RemoveGuestAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("não encontrado");
    }

    [Fact]
    public async Task RemoveGuestAsync_WhenAllowGuestsFalse_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveGuestAsync_WhenAllowGuestsFalse_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakeEventPoll(groupId, allowGuests: false);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.RemoveGuestAsync(groupId, poll.Id, Guid.NewGuid(), Guid.NewGuid(), false);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveGuestAsync_WhenPollClosed_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveGuestAsync_WhenPollClosed_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakeEventPoll(groupId, allowGuests: true, status: "closed");
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.RemoveGuestAsync(groupId, poll.Id, Guid.NewGuid(), Guid.NewGuid(), false);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("encerrado");
    }

    [Fact]
    public async Task RemoveGuestAsync_WhenGuestNotFound_ShouldReturnOkIdempotent()
    {
        // Remoção de convidado inexistente deve ser idempotente (não retorna erro)
        await using var db = DbContextFactory.Create(nameof(RemoveGuestAsync_WhenGuestNotFound_ShouldReturnOkIdempotent));
        var groupId = Guid.NewGuid();
        var poll = MakeEventPoll(groupId, allowGuests: true);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.RemoveGuestAsync(groupId, poll.Id, Guid.NewGuid(), Guid.NewGuid(), false);

        result.Success.Should().BeTrue("remover convidado que não existe deve ser idempotente.");
    }

    [Fact]
    public async Task RemoveGuestAsync_WhenOtherPlayersGuest_NonAdmin_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveGuestAsync_WhenOtherPlayersGuest_NonAdmin_ShouldReturnFailure));
        var groupId  = Guid.NewGuid();
        var owner    = MakePlayer(groupId, "Dono");
        var stranger = MakePlayer(groupId, "Estranho");
        var poll     = MakeEventPoll(groupId, allowGuests: true);
        db.Players.Add(owner);
        db.Players.Add(stranger);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var guest = new PollGuestEntity(poll.Id, owner.Id, "Convidado do Dono", true);
        db.PollGuests.Add(guest);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.RemoveGuestAsync(groupId, poll.Id, guest.Id, stranger.Id, isAdmin: false);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("próprios convidados");
    }

    [Fact]
    public async Task RemoveGuestAsync_WhenOwnGuest_ShouldDeleteAndReturnOk()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveGuestAsync_WhenOwnGuest_ShouldDeleteAndReturnOk));
        var groupId = Guid.NewGuid();
        var player  = MakePlayer(groupId);
        var poll    = MakeEventPoll(groupId, allowGuests: true);
        db.Players.Add(player);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var guest = new PollGuestEntity(poll.Id, player.Id, "Meu Convidado", false);
        db.PollGuests.Add(guest);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.RemoveGuestAsync(groupId, poll.Id, guest.Id, player.Id, isAdmin: false);

        result.Success.Should().BeTrue();
        var removed = await db.PollGuests.FindAsync(guest.Id);
        removed.Should().BeNull("o convidado deve ter sido excluído do banco.");
    }

    [Fact]
    public async Task RemoveGuestAsync_WhenAdminRemovesOtherPlayersGuest_ShouldSucceed()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveGuestAsync_WhenAdminRemovesOtherPlayersGuest_ShouldSucceed));
        var groupId = Guid.NewGuid();
        var owner   = MakePlayer(groupId);
        var poll    = MakeEventPoll(groupId, allowGuests: true);
        db.Players.Add(owner);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var guest = new PollGuestEntity(poll.Id, owner.Id, "Convidado", true);
        db.PollGuests.Add(guest);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var adminId = Guid.NewGuid();
        var result  = await sut.RemoveGuestAsync(groupId, poll.Id, guest.Id, adminId, isAdmin: true);

        result.Success.Should().BeTrue("admin pode remover convidado de qualquer jogador.");
        var removed = await db.PollGuests.FindAsync(guest.Id);
        removed.Should().BeNull();
    }

    [Fact]
    public async Task RemoveGuestAsync_WhenDeadlineExpired_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveGuestAsync_WhenDeadlineExpired_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var player  = MakePlayer(groupId);
        var past    = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        var poll    = MakeEventPoll(groupId, allowGuests: true, deadlineDate: past);
        db.Players.Add(player);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        var guest = new PollGuestEntity(poll.Id, player.Id, "Convidado", true);
        db.PollGuests.Add(guest);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.RemoveGuestAsync(groupId, poll.Id, guest.Id, player.Id, isAdmin: false);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("prazo");
    }
}
