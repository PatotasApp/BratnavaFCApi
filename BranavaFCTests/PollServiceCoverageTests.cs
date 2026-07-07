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
/// Testes de cobertura adicionais para PollService: GetPollAsync, CreatePollAsync,
/// CreateEventPollAsync, ClosePollAsync, ReopenPollAsync, UpdatePollDetailsAsync,
/// SetShowVotesAsync, DeletePollAsync, opções (Add/Update/Delete), votos
/// (Cast/AdminCast/Remove) e GetPollsAsync.
/// </summary>
public class PollServiceCoverageTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PollService BuildSut(
        BratnavaFC.Infrastructure.Data.AppDbContext db,
        IPushService? push = null,
        INotificationScheduler? scheduler = null)
        => new(db,
               push      ?? Mock.Of<IPushService>(),
               scheduler ?? Mock.Of<INotificationScheduler>(),
               Mock.Of<ILogger<PollService>>());

    private static PollEntity MakePoll(
        Guid groupId,
        bool allowMultipleVotes = false,
        bool showVotes = false,
        string status = "open",
        DateOnly? deadlineDate = null,
        TimeOnly? deadlineTime = null)
    {
        var poll = new PollEntity(
            groupId, "Votação de cobertura", "desc",
            allowMultipleVotes, showVotes,
            createdByUserId: Guid.NewGuid(),
            deadlineDate: deadlineDate,
            deadlineTime: deadlineTime);
        if (status == "closed") poll.Close();
        return poll;
    }

    private static PollEntity MakeEventPoll(Guid groupId, bool allowGuests = true)
        => new(groupId, "Evento de cobertura", null,
               allowMultipleVotes: false, showVotes: true,
               createdByUserId: Guid.NewGuid(),
               type: "event", eventDate: new DateOnly(2030, 5, 1),
               allowGuests: allowGuests);

    private static PlayerEntity MakeMember(Guid groupId, string name = "Membro")
        => new(name, Guid.NewGuid(), groupId, 1m, false);

    private static GroupEntity MakeGroup() => new("Patota FC", null, Guid.NewGuid());

    // ── GetPollAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPollAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.GetPollAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("não encontrada");
    }

    [Fact]
    public async Task GetPollAsync_HappyPath_ShouldReturnOptionsVotesAndCounts()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollAsync_HappyPath_ShouldReturnOptionsVotesAndCounts));
        var groupId = Guid.NewGuid();
        var poll    = MakePoll(groupId, showVotes: true);
        var opt1    = new PollOptionEntity(poll.Id, "Opção A", null, null, 0);
        var opt2    = new PollOptionEntity(poll.Id, "Opção B", null, null, 1);
        var voter   = MakeMember(groupId, "Fulano");
        var other   = MakeMember(groupId, "Ciclano");
        db.Polls.Add(poll);
        db.PollOptions.AddRange(opt1, opt2);
        db.Players.AddRange(voter, other);
        db.PollVotes.Add(new PollVoteEntity(poll.Id, opt1.Id, voter.Id));
        db.PollVotes.Add(new PollVoteEntity(poll.Id, opt2.Id, other.Id));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.GetPollAsync(groupId, poll.Id, voter.Id, isAdmin: false);

        result.Success.Should().BeTrue();
        var dto = result.Data!;
        dto.Options.Should().HaveCount(2);
        dto.Options[0].Text.Should().Be("Opção A");
        dto.Options[0].VoteCount.Should().Be(1);
        dto.Options[1].VoteCount.Should().Be(1);
        dto.TotalVoters.Should().Be(2);
        dto.MyVotedOptionIds.Should().ContainSingle().Which.Should().Be(opt1.Id);
        dto.Votes.Should().HaveCount(2, "showVotes=true expõe todos os votos.");
        dto.IsAcceptingVotes.Should().BeTrue();
        dto.Members.Should().BeNull("não-admin não recebe a visão de membros.");
    }

    [Fact]
    public async Task GetPollAsync_WhenShowVotesFalse_NonAdminVoter_ShouldSeeOnlyOwnVotes()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollAsync_WhenShowVotesFalse_NonAdminVoter_ShouldSeeOnlyOwnVotes));
        var groupId = Guid.NewGuid();
        var poll    = MakePoll(groupId, showVotes: false);
        var opt     = new PollOptionEntity(poll.Id, "Única", null, null, 0);
        var me      = MakeMember(groupId, "Eu");
        var other   = MakeMember(groupId, "Outro");
        db.Polls.Add(poll);
        db.PollOptions.Add(opt);
        db.Players.AddRange(me, other);
        db.PollVotes.Add(new PollVoteEntity(poll.Id, opt.Id, me.Id));
        db.PollVotes.Add(new PollVoteEntity(poll.Id, opt.Id, other.Id));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.GetPollAsync(groupId, poll.Id, me.Id, isAdmin: false);

        result.Success.Should().BeTrue();
        result.Data!.Votes.Should().ContainSingle().Which.PlayerId.Should().Be(me.Id);
    }

    [Fact]
    public async Task GetPollAsync_WhenShowVotesFalse_NonAdminWithoutVote_VotesShouldBeNull()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollAsync_WhenShowVotesFalse_NonAdminWithoutVote_VotesShouldBeNull));
        var groupId = Guid.NewGuid();
        var poll    = MakePoll(groupId, showVotes: false);
        var opt     = new PollOptionEntity(poll.Id, "Única", null, null, 0);
        var other   = MakeMember(groupId, "Outro");
        db.Polls.Add(poll);
        db.PollOptions.Add(opt);
        db.Players.Add(other);
        db.PollVotes.Add(new PollVoteEntity(poll.Id, opt.Id, other.Id));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.GetPollAsync(groupId, poll.Id, Guid.NewGuid(), isAdmin: false);

        result.Success.Should().BeTrue();
        result.Data!.Votes.Should().BeNull("quem não votou não vê votos quando showVotes=false.");
    }

    [Fact]
    public async Task GetPollAsync_WhenAdmin_ShouldIncludeMembersWithAndWithoutVotes()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollAsync_WhenAdmin_ShouldIncludeMembersWithAndWithoutVotes));
        var groupId  = Guid.NewGuid();
        var poll     = MakePoll(groupId, showVotes: false);
        var opt      = new PollOptionEntity(poll.Id, "Sim", null, null, 0);
        var voter    = MakeMember(groupId, "Ana");
        var nonVoter = MakeMember(groupId, "Bruno");
        // Guest não deve aparecer na lista de membros
        var guest = new PlayerEntity("Convidado", null, groupId, 1m, false, isGuest: true);
        db.Polls.Add(poll);
        db.PollOptions.Add(opt);
        db.Players.AddRange(voter, nonVoter, guest);
        db.PollVotes.Add(new PollVoteEntity(poll.Id, opt.Id, voter.Id));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.GetPollAsync(groupId, poll.Id, Guid.Empty, isAdmin: true);

        result.Success.Should().BeTrue();
        var members = result.Data!.Members;
        members.Should().NotBeNull();
        members!.Should().HaveCount(2, "somente membros ativos com usuário vinculado (sem guests).");
        members.Single(m => m.PlayerId == voter.Id).VotedOptionIds.Should().ContainSingle().Which.Should().Be(opt.Id);
        members.Single(m => m.PlayerId == nonVoter.Id).VotedOptionIds.Should().BeEmpty();
        result.Data!.Votes.Should().HaveCount(1, "admin vê todos os votos mesmo com showVotes=false.");
    }

    [Fact]
    public async Task GetPollAsync_SkipImages_ShouldReturnEmptyImageLists()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollAsync_SkipImages_ShouldReturnEmptyImageLists));
        var groupId = Guid.NewGuid();
        var poll    = MakePoll(groupId);
        var opt     = new PollOptionEntity(poll.Id, "Com imagem", null, null, 0);
        db.Polls.Add(poll);
        db.PollOptions.Add(opt);
        db.PollOptionImages.Add(new PollOptionImageEntity(opt.Id, "data:image/png;base64,AAA", 0));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var withImages = await sut.GetPollAsync(groupId, poll.Id, Guid.NewGuid(), false, CancellationToken.None, skipImages: false);
        var noImages   = await sut.GetPollAsync(groupId, poll.Id, Guid.NewGuid(), false, CancellationToken.None, skipImages: true);

        withImages.Data!.Options[0].Images.Should().ContainSingle();
        noImages.Data!.Options[0].Images.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPollAsync_EventWithGuests_ShouldAttachGuestsToVoterVotes()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollAsync_EventWithGuests_ShouldAttachGuestsToVoterVotes));
        var groupId = Guid.NewGuid();
        var poll    = MakeEventPoll(groupId);
        var opt     = new PollOptionEntity(poll.Id, "Sim", null, null, 0);
        var voter   = MakeMember(groupId, "Dono");
        db.Polls.Add(poll);
        db.PollOptions.Add(opt);
        db.Players.Add(voter);
        db.PollVotes.Add(new PollVoteEntity(poll.Id, opt.Id, voter.Id));
        db.PollGuests.Add(new PollGuestEntity(poll.Id, voter.Id, "Convidado 1", true));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.GetPollAsync(groupId, poll.Id, voter.Id, isAdmin: false);

        result.Success.Should().BeTrue();
        var vote = result.Data!.Votes!.Single();
        vote.Guests.Should().ContainSingle().Which.GuestName.Should().Be("Convidado 1");
        result.Data!.AllowGuests.Should().BeTrue();
    }

    // ── GetPollsAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPollsAsync_ShouldFilterByTypeAndReportHasVoted()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollsAsync_ShouldFilterByTypeAndReportHasVoted));
        var groupId = Guid.NewGuid();
        var poll    = MakePoll(groupId);
        var ev      = MakeEventPoll(groupId);
        var opt     = new PollOptionEntity(poll.Id, "A", null, null, 0);
        var player  = MakeMember(groupId);
        db.Polls.AddRange(poll, ev);
        db.PollOptions.Add(opt);
        db.Players.Add(player);
        db.PollVotes.Add(new PollVoteEntity(poll.Id, opt.Id, player.Id));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var all       = await sut.GetPollsAsync(groupId, player.Id);
        var onlyPolls = await sut.GetPollsAsync(groupId, player.Id, type: "poll");

        all.Data!.Total.Should().Be(2);
        onlyPolls.Data!.Total.Should().Be(1);
        var summary = onlyPolls.Data!.Items.Single();
        summary.HasVoted.Should().BeTrue();
        summary.OptionCount.Should().Be(1);
        summary.TotalVoters.Should().Be(1);
    }

    // ── CreatePollAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePollAsync_WhenGroupNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(CreatePollAsync_WhenGroupNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.CreatePollAsync(Guid.NewGuid(), Guid.NewGuid(), new CreatePollDto { Title = "X" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Grupo");
    }

    [Fact]
    public async Task CreatePollAsync_HappyPath_ShouldPersistNotifyAndSchedule()
    {
        await using var db = DbContextFactory.Create(nameof(CreatePollAsync_HappyPath_ShouldPersistNotifyAndSchedule));
        var group = MakeGroup();
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        var push      = new Mock<IPushService>();
        var scheduler = new Mock<INotificationScheduler>();
        var sut = BuildSut(db, push.Object, scheduler.Object);

        var dto = new CreatePollDto
        {
            Title = "Nova votação", Description = "d",
            AllowMultipleVotes = true, ShowVotes = true,
            DeadlineDate = "2030-08-01", DeadlineTime = "19:30",
        };
        var result = await sut.CreatePollAsync(group.Id, Guid.NewGuid(), dto);

        result.Success.Should().BeTrue();
        result.Data!.Title.Should().Be("Nova votação");
        result.Data!.DeadlineDate.Should().Be("2030-08-01");
        result.Data!.DeadlineTime.Should().Be("19:30");
        (await db.Polls.CountAsync(p => p.GroupId == group.Id)).Should().Be(1);
        push.Verify(p => p.SendToGroupAsync(group.Id, It.Is<string>(t => t.Contains("Nova votação")),
            It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        scheduler.Verify(s => s.SchedulePollRemindersAsync(It.IsAny<Guid>(), group.Id, "Nova votação",
            new DateOnly(2030, 8, 1), new TimeOnly(19, 30), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreatePollAsync_WithAddToCalendarAndDeadline_ShouldCreateCalendarReminder()
    {
        await using var db = DbContextFactory.Create(nameof(CreatePollAsync_WithAddToCalendarAndDeadline_ShouldCreateCalendarReminder));
        var group = MakeGroup();
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var dto = new CreatePollDto { Title = "Com lembrete", DeadlineDate = "2030-09-10", AddToCalendar = true };
        var result = await sut.CreatePollAsync(group.Id, Guid.NewGuid(), dto);

        result.Success.Should().BeTrue();
        var ev = await db.CalendarEvents.SingleAsync(e => e.GroupId == group.Id);
        ev.Title.Should().Contain("Com lembrete");
        ev.EventDate.Should().Be(new DateOnly(2030, 9, 10));
    }

    [Fact]
    public async Task CreatePollAsync_WithAddToCalendarButNoDeadline_ShouldNotCreateCalendarReminder()
    {
        await using var db = DbContextFactory.Create(nameof(CreatePollAsync_WithAddToCalendarButNoDeadline_ShouldNotCreateCalendarReminder));
        var group = MakeGroup();
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.CreatePollAsync(group.Id, Guid.NewGuid(),
            new CreatePollDto { Title = "Sem prazo", AddToCalendar = true });

        result.Success.Should().BeTrue();
        (await db.CalendarEvents.CountAsync()).Should().Be(0);
    }

    // ── CreateEventPollAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateEventPollAsync_WhenGroupNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(CreateEventPollAsync_WhenGroupNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.CreateEventPollAsync(Guid.NewGuid(), Guid.NewGuid(),
            new CreateEventPollDto { Title = "E", EventDate = "2030-01-01" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Grupo");
    }

    [Fact]
    public async Task CreateEventPollAsync_WhenEventDateInvalid_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(CreateEventPollAsync_WhenEventDateInvalid_ShouldReturnFailure));
        var group = MakeGroup();
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.CreateEventPollAsync(group.Id, Guid.NewGuid(),
            new CreateEventPollDto { Title = "E", EventDate = "not-a-date" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Data do evento");
    }

    [Fact]
    public async Task CreateEventPollAsync_HappyPath_ShouldCreateThreeFixedOptions()
    {
        await using var db = DbContextFactory.Create(nameof(CreateEventPollAsync_HappyPath_ShouldCreateThreeFixedOptions));
        var group = MakeGroup();
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var dto = new CreateEventPollDto
        {
            Title = "Churrasco", EventDate = "2030-04-20", EventTime = "12:00",
            EventLocation = "Sede", EventIcon = "🍖", CostType = "individual", CostAmount = 30m,
            DeadlineDate = "2030-04-18", AllowGuests = true,
        };
        var result = await sut.CreateEventPollAsync(group.Id, Guid.NewGuid(), dto);

        result.Success.Should().BeTrue();
        var poll = result.Data!;
        poll.Type.Should().Be("event");
        poll.EventDate.Should().Be("2030-04-20");
        poll.EventTime.Should().Be("12:00");
        poll.EventLocation.Should().Be("Sede");
        poll.CostAmount.Should().Be(30m);
        poll.AllowGuests.Should().BeTrue();
        poll.Options.Select(o => o.Text).Should().ContainInOrder("Sim", "Talvez", "Não");
    }

    // ── ClosePollAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ClosePollAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(ClosePollAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.ClosePollAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new ClosePollDto());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("não encontrada");
    }

    [Fact]
    public async Task ClosePollAsync_HappyPath_ShouldCloseAndNotifyOnce()
    {
        await using var db = DbContextFactory.Create(nameof(ClosePollAsync_HappyPath_ShouldCloseAndNotifyOnce));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var push = new Mock<IPushService>();
        var sut = BuildSut(db, push.Object);

        var result = await sut.ClosePollAsync(groupId, poll.Id, Guid.NewGuid(), new ClosePollDto());

        result.Success.Should().BeTrue();
        (await db.Polls.FirstAsync(p => p.Id == poll.Id)).Status.Should().Be("closed");
        push.Verify(p => p.SendToGroupAsync(groupId, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClosePollAsync_WithCreateEvent_ShouldAddCalendarEventAndNotifyTwice()
    {
        await using var db = DbContextFactory.Create(nameof(ClosePollAsync_WithCreateEvent_ShouldAddCalendarEventAndNotifyTwice));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var push = new Mock<IPushService>();
        var sut = BuildSut(db, push.Object);

        var dto = new ClosePollDto
        {
            CreateEvent = true, EventTitle = "Pelada decidida",
            EventDate = "2030-10-05", EventTime = "16:00", EventIcon = "⚽",
        };
        var result = await sut.ClosePollAsync(groupId, poll.Id, Guid.NewGuid(), dto);

        result.Success.Should().BeTrue();
        var ev = await db.CalendarEvents.SingleAsync(e => e.GroupId == groupId);
        ev.Title.Should().Be("Pelada decidida");
        ev.EventDate.Should().Be(new DateOnly(2030, 10, 5));
        push.Verify(p => p.SendToGroupAsync(groupId, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ClosePollAsync_WithCreateEventButInvalidDate_ShouldCloseWithoutCalendarEvent()
    {
        await using var db = DbContextFactory.Create(nameof(ClosePollAsync_WithCreateEventButInvalidDate_ShouldCloseWithoutCalendarEvent));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var dto = new ClosePollDto { CreateEvent = true, EventTitle = "T", EventDate = "banana" };
        var result = await sut.ClosePollAsync(groupId, poll.Id, Guid.NewGuid(), dto);

        result.Success.Should().BeTrue();
        (await db.CalendarEvents.CountAsync()).Should().Be(0);
        (await db.Polls.FirstAsync(p => p.Id == poll.Id)).Status.Should().Be("closed");
    }

    // ── ReopenPollAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ReopenPollAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(ReopenPollAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.ReopenPollAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ReopenPollAsync_HappyPath_ShouldSetStatusOpen()
    {
        await using var db = DbContextFactory.Create(nameof(ReopenPollAsync_HappyPath_ShouldSetStatusOpen));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId, status: "closed");
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.ReopenPollAsync(groupId, poll.Id);

        result.Success.Should().BeTrue();
        (await db.Polls.FirstAsync(p => p.Id == poll.Id)).Status.Should().Be("open");
    }

    // ── UpdatePollDetailsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePollDetailsAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(UpdatePollDetailsAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.UpdatePollDetailsAsync(Guid.NewGuid(), Guid.NewGuid(), new UpdatePollDetailsDto());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("não encontrada");
    }

    [Fact]
    public async Task UpdatePollDetailsAsync_HappyPath_ShouldUpdateAndReturnSummary()
    {
        await using var db = DbContextFactory.Create(nameof(UpdatePollDetailsAsync_HappyPath_ShouldUpdateAndReturnSummary));
        var groupId = Guid.NewGuid();
        var poll = MakeEventPoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var dto = new UpdatePollDetailsDto { Description = "Nova desc", CostAmount = 55m, CostType = "group" };
        var result = await sut.UpdatePollDetailsAsync(groupId, poll.Id, dto);

        result.Success.Should().BeTrue();
        result.Data!.Description.Should().Be("Nova desc");
        result.Data!.CostAmount.Should().Be(55m);
        result.Data!.CostType.Should().Be("group");
        var reloaded = await db.Polls.FirstAsync(p => p.Id == poll.Id);
        reloaded.Description.Should().Be("Nova desc");
    }

    // ── SetShowVotesAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task SetShowVotesAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(SetShowVotesAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.SetShowVotesAsync(Guid.NewGuid(), Guid.NewGuid(), true);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SetShowVotesAsync_HappyPath_ShouldPersistFlag()
    {
        await using var db = DbContextFactory.Create(nameof(SetShowVotesAsync_HappyPath_ShouldPersistFlag));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId, showVotes: false);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.SetShowVotesAsync(groupId, poll.Id, true);

        result.Success.Should().BeTrue();
        (await db.Polls.FirstAsync(p => p.Id == poll.Id)).ShowVotes.Should().BeTrue();
    }

    // ── DeletePollAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeletePollAsync_WhenPollNotFound_ShouldBeIdempotentOk()
    {
        await using var db = DbContextFactory.Create(nameof(DeletePollAsync_WhenPollNotFound_ShouldBeIdempotentOk));
        var sut = BuildSut(db);

        var result = await sut.DeletePollAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Success.Should().BeTrue("excluir votação inexistente é idempotente.");
    }

    [Fact]
    public async Task DeletePollAsync_HappyPath_ShouldRemoveAndCancelReminders()
    {
        await using var db = DbContextFactory.Create(nameof(DeletePollAsync_HappyPath_ShouldRemoveAndCancelReminders));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var scheduler = new Mock<INotificationScheduler>();
        var sut = BuildSut(db, scheduler: scheduler.Object);

        var result = await sut.DeletePollAsync(groupId, poll.Id);

        result.Success.Should().BeTrue();
        (await db.Polls.AnyAsync(p => p.Id == poll.Id)).Should().BeFalse();
        scheduler.Verify(s => s.CancelPollRemindersAsync(poll.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── AddOptionAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task AddOptionAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(AddOptionAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.AddOptionAsync(Guid.NewGuid(), Guid.NewGuid(), new AddPollOptionDto { Text = "X" });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task AddOptionAsync_WhenEventPoll_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(AddOptionAsync_WhenEventPoll_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakeEventPoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.AddOptionAsync(groupId, poll.Id, new AddPollOptionDto { Text = "X" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("eventos");
    }

    [Fact]
    public async Task AddOptionAsync_HappyPath_ShouldAppendWithNextSortOrder()
    {
        await using var db = DbContextFactory.Create(nameof(AddOptionAsync_HappyPath_ShouldAppendWithNextSortOrder));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        db.PollOptions.Add(new PollOptionEntity(poll.Id, "Existente", null, null, 0));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.AddOptionAsync(groupId, poll.Id, new AddPollOptionDto { Text = "Nova", Description = "d" });

        result.Success.Should().BeTrue();
        result.Data!.Text.Should().Be("Nova");
        result.Data!.SortOrder.Should().Be(1);
        result.Data!.Images.Should().BeEmpty();
        (await db.PollOptions.CountAsync(o => o.PollId == poll.Id)).Should().Be(2);
    }

    [Fact]
    public async Task AddOptionAsync_WithImages_ShouldPersistImageEntities()
    {
        await using var db = DbContextFactory.Create(nameof(AddOptionAsync_WithImages_ShouldPersistImageEntities));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var dto = new AddPollOptionDto { Text = "Com fotos", Images = ["img1", "img2"] };
        var result = await sut.AddOptionAsync(groupId, poll.Id, dto);

        result.Success.Should().BeTrue();
        result.Data!.Images.Should().HaveCount(2);
        (await db.PollOptionImages.CountAsync(i => i.OptionId == result.Data!.Id)).Should().Be(2);
    }

    // ── UpdateOptionAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateOptionAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateOptionAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.UpdateOptionAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new UpdatePollOptionDto());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateOptionAsync_WhenEventPoll_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateOptionAsync_WhenEventPoll_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakeEventPoll(groupId);
        var opt  = new PollOptionEntity(poll.Id, "Sim", null, null, 0);
        db.Polls.Add(poll);
        db.PollOptions.Add(opt);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.UpdateOptionAsync(groupId, poll.Id, opt.Id, new UpdatePollOptionDto { Text = "N" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("eventos");
    }

    [Fact]
    public async Task UpdateOptionAsync_WhenOptionNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateOptionAsync_WhenOptionNotFound_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.UpdateOptionAsync(groupId, poll.Id, Guid.NewGuid(), new UpdatePollOptionDto());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Opção");
    }

    [Fact]
    public async Task UpdateOptionAsync_WhenImagesNull_ShouldPreserveExistingImages()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateOptionAsync_WhenImagesNull_ShouldPreserveExistingImages));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        var opt  = new PollOptionEntity(poll.Id, "Original", null, null, 0);
        db.Polls.Add(poll);
        db.PollOptions.Add(opt);
        db.PollOptionImages.Add(new PollOptionImageEntity(opt.Id, "keep-me", 0));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.UpdateOptionAsync(groupId, poll.Id, opt.Id,
            new UpdatePollOptionDto { Text = "Renomeada", Images = null });

        result.Success.Should().BeTrue();
        result.Data!.Text.Should().Be("Renomeada");
        result.Data!.Images.Should().ContainSingle().Which.Should().Be("keep-me");
    }

    [Fact]
    public async Task UpdateOptionAsync_WithImagesList_ShouldReplaceImages()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateOptionAsync_WithImagesList_ShouldReplaceImages));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        var opt  = new PollOptionEntity(poll.Id, "Original", null, null, 0);
        db.Polls.Add(poll);
        db.PollOptions.Add(opt);
        db.PollOptionImages.Add(new PollOptionImageEntity(opt.Id, "old", 0));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.UpdateOptionAsync(groupId, poll.Id, opt.Id,
            new UpdatePollOptionDto { Images = ["new1", "new2"] });

        result.Success.Should().BeTrue();
        result.Data!.Images.Should().Equal("new1", "new2");
        var persisted = await db.PollOptionImages.Where(i => i.OptionId == opt.Id).Select(i => i.ImageUrl).ToListAsync();
        persisted.Should().BeEquivalentTo(["new1", "new2"]);
    }

    // ── DeleteOptionAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteOptionAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteOptionAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.DeleteOptionAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteOptionAsync_WhenEventPoll_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteOptionAsync_WhenEventPoll_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakeEventPoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.DeleteOptionAsync(groupId, poll.Id, Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("eventos");
    }

    [Fact]
    public async Task DeleteOptionAsync_WhenOptionNotFound_ShouldBeIdempotentOk()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteOptionAsync_WhenOptionNotFound_ShouldBeIdempotentOk));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.DeleteOptionAsync(groupId, poll.Id, Guid.NewGuid());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteOptionAsync_HappyPath_ShouldRemoveOption()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteOptionAsync_HappyPath_ShouldRemoveOption));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        var opt  = new PollOptionEntity(poll.Id, "Remover", null, null, 0);
        db.Polls.Add(poll);
        db.PollOptions.Add(opt);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.DeleteOptionAsync(groupId, poll.Id, opt.Id);

        result.Success.Should().BeTrue();
        (await db.PollOptions.AnyAsync(o => o.Id == opt.Id)).Should().BeFalse();
    }

    // ── CastVoteAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CastVoteAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(CastVoteAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.CastVoteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new CastVoteDto { OptionIds = [Guid.NewGuid()] });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CastVoteAsync_WhenPollClosed_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(CastVoteAsync_WhenPollClosed_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId, status: "closed");
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.CastVoteAsync(groupId, poll.Id, Guid.NewGuid(),
            new CastVoteDto { OptionIds = [Guid.NewGuid()] });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("encerrada");
    }

    [Fact]
    public async Task CastVoteAsync_WhenDeadlinePassed_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(CastVoteAsync_WhenDeadlinePassed_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId, deadlineDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)));
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.CastVoteAsync(groupId, poll.Id, Guid.NewGuid(),
            new CastVoteDto { OptionIds = [Guid.NewGuid()] });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("prazo");
    }

    [Fact]
    public async Task CastVoteAsync_WhenMultipleVotesNotAllowed_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(CastVoteAsync_WhenMultipleVotesNotAllowed_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId, allowMultipleVotes: false);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.CastVoteAsync(groupId, poll.Id, Guid.NewGuid(),
            new CastVoteDto { OptionIds = [Guid.NewGuid(), Guid.NewGuid()] });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("apenas uma opção");
    }

    [Fact]
    public async Task CastVoteAsync_WhenOptionInvalid_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(CastVoteAsync_WhenOptionInvalid_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        db.PollOptions.Add(new PollOptionEntity(poll.Id, "Real", null, null, 0));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.CastVoteAsync(groupId, poll.Id, Guid.NewGuid(),
            new CastVoteDto { OptionIds = [Guid.NewGuid()] });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Opção inválida");
    }

    [Fact]
    public async Task CastVoteAsync_HappyPath_ShouldReplacePreviousVote()
    {
        await using var db = DbContextFactory.Create(nameof(CastVoteAsync_HappyPath_ShouldReplacePreviousVote));
        var groupId = Guid.NewGuid();
        var poll   = MakePoll(groupId);
        var opt1   = new PollOptionEntity(poll.Id, "A", null, null, 0);
        var opt2   = new PollOptionEntity(poll.Id, "B", null, null, 1);
        var player = MakeMember(groupId);
        db.Polls.Add(poll);
        db.PollOptions.AddRange(opt1, opt2);
        db.Players.Add(player);
        db.PollVotes.Add(new PollVoteEntity(poll.Id, opt1.Id, player.Id));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.CastVoteAsync(groupId, poll.Id, player.Id,
            new CastVoteDto { OptionIds = [opt2.Id] });

        result.Success.Should().BeTrue();
        result.Data!.MyVotedOptionIds.Should().ContainSingle().Which.Should().Be(opt2.Id);
        var votes = await db.PollVotes.Where(v => v.PollId == poll.Id && v.PlayerId == player.Id).ToListAsync();
        votes.Should().ContainSingle().Which.OptionId.Should().Be(opt2.Id);
    }

    [Fact]
    public async Task CastVoteAsync_WhenMultipleVotesAllowed_ShouldStoreAllDistinctVotes()
    {
        await using var db = DbContextFactory.Create(nameof(CastVoteAsync_WhenMultipleVotesAllowed_ShouldStoreAllDistinctVotes));
        var groupId = Guid.NewGuid();
        var poll   = MakePoll(groupId, allowMultipleVotes: true);
        var opt1   = new PollOptionEntity(poll.Id, "A", null, null, 0);
        var opt2   = new PollOptionEntity(poll.Id, "B", null, null, 1);
        var player = MakeMember(groupId);
        db.Polls.Add(poll);
        db.PollOptions.AddRange(opt1, opt2);
        db.Players.Add(player);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        // opt1 duplicado — Distinct deve deduplicar
        var result = await sut.CastVoteAsync(groupId, poll.Id, player.Id,
            new CastVoteDto { OptionIds = [opt1.Id, opt2.Id, opt1.Id] });

        result.Success.Should().BeTrue();
        var votes = await db.PollVotes.Where(v => v.PollId == poll.Id && v.PlayerId == player.Id).ToListAsync();
        votes.Should().HaveCount(2);
    }

    // ── AdminCastVoteAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task AdminCastVoteAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(AdminCastVoteAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.AdminCastVoteAsync(Guid.NewGuid(), Guid.NewGuid(),
            new AdminCastVoteDto { PlayerId = Guid.NewGuid(), OptionIds = [Guid.NewGuid()] });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task AdminCastVoteAsync_WhenMultipleOnSingleVotePoll_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(AdminCastVoteAsync_WhenMultipleOnSingleVotePoll_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId, allowMultipleVotes: false);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.AdminCastVoteAsync(groupId, poll.Id,
            new AdminCastVoteDto { PlayerId = Guid.NewGuid(), OptionIds = [Guid.NewGuid(), Guid.NewGuid()] });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("apenas uma opção");
    }

    [Fact]
    public async Task AdminCastVoteAsync_WhenOptionInvalid_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(AdminCastVoteAsync_WhenOptionInvalid_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.AdminCastVoteAsync(groupId, poll.Id,
            new AdminCastVoteDto { PlayerId = Guid.NewGuid(), OptionIds = [Guid.NewGuid()] });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Opção inválida");
    }

    [Fact]
    public async Task AdminCastVoteAsync_WithEmptyOptionIds_ShouldRemoveExistingVotes()
    {
        await using var db = DbContextFactory.Create(nameof(AdminCastVoteAsync_WithEmptyOptionIds_ShouldRemoveExistingVotes));
        var groupId = Guid.NewGuid();
        var poll   = MakePoll(groupId);
        var opt    = new PollOptionEntity(poll.Id, "A", null, null, 0);
        var player = MakeMember(groupId);
        db.Polls.Add(poll);
        db.PollOptions.Add(opt);
        db.Players.Add(player);
        db.PollVotes.Add(new PollVoteEntity(poll.Id, opt.Id, player.Id));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.AdminCastVoteAsync(groupId, poll.Id,
            new AdminCastVoteDto { PlayerId = player.Id, OptionIds = [] });

        result.Success.Should().BeTrue();
        (await db.PollVotes.AnyAsync(v => v.PollId == poll.Id && v.PlayerId == player.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task AdminCastVoteAsync_OnClosedPoll_ShouldBypassStatusAndPersistVote()
    {
        await using var db = DbContextFactory.Create(nameof(AdminCastVoteAsync_OnClosedPoll_ShouldBypassStatusAndPersistVote));
        var groupId = Guid.NewGuid();
        var poll   = MakePoll(groupId, status: "closed");
        var opt    = new PollOptionEntity(poll.Id, "A", null, null, 0);
        var player = MakeMember(groupId);
        db.Polls.Add(poll);
        db.PollOptions.Add(opt);
        db.Players.Add(player);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.AdminCastVoteAsync(groupId, poll.Id,
            new AdminCastVoteDto { PlayerId = player.Id, OptionIds = [opt.Id] });

        result.Success.Should().BeTrue("admin pode votar em nome de jogador mesmo com a votação encerrada.");
        (await db.PollVotes.CountAsync(v => v.PollId == poll.Id && v.PlayerId == player.Id)).Should().Be(1);
    }

    // ── RemoveVoteAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveVoteAsync_WhenPollNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveVoteAsync_WhenPollNotFound_ShouldReturnFailure));
        var sut = BuildSut(db);

        var result = await sut.RemoveVoteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveVoteAsync_WhenPollClosed_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveVoteAsync_WhenPollClosed_ShouldReturnFailure));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId, status: "closed");
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.RemoveVoteAsync(groupId, poll.Id, Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("encerrada");
    }

    [Fact]
    public async Task RemoveVoteAsync_HappyPath_ShouldDeleteAllPlayerVotes()
    {
        await using var db = DbContextFactory.Create(nameof(RemoveVoteAsync_HappyPath_ShouldDeleteAllPlayerVotes));
        var groupId = Guid.NewGuid();
        var poll   = MakePoll(groupId, allowMultipleVotes: true);
        var opt1   = new PollOptionEntity(poll.Id, "A", null, null, 0);
        var opt2   = new PollOptionEntity(poll.Id, "B", null, null, 1);
        var player = MakeMember(groupId);
        db.Polls.Add(poll);
        db.PollOptions.AddRange(opt1, opt2);
        db.Players.Add(player);
        db.PollVotes.Add(new PollVoteEntity(poll.Id, opt1.Id, player.Id));
        db.PollVotes.Add(new PollVoteEntity(poll.Id, opt2.Id, player.Id));
        await db.SaveChangesAsync();
        var sut = BuildSut(db);

        var result = await sut.RemoveVoteAsync(groupId, poll.Id, player.Id);

        result.Success.Should().BeTrue();
        result.Data!.MyVotedOptionIds.Should().BeEmpty();
        (await db.PollVotes.AnyAsync(v => v.PollId == poll.Id && v.PlayerId == player.Id)).Should().BeFalse();
    }

    // ── UpdateDeadlineAsync (agendamento) ─────────────────────────────────────

    [Fact]
    public async Task UpdateDeadlineAsync_WhenSettingDeadline_ShouldRescheduleReminders()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateDeadlineAsync_WhenSettingDeadline_ShouldRescheduleReminders));
        var groupId = Guid.NewGuid();
        var poll = MakePoll(groupId);
        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        var scheduler = new Mock<INotificationScheduler>();
        var sut = BuildSut(db, scheduler: scheduler.Object);

        var result = await sut.UpdateDeadlineAsync(groupId, poll.Id,
            new UpdatePollDeadlineDto { DeadlineDate = "2030-11-20", DeadlineTime = "21:00" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        scheduler.Verify(s => s.ReschedulePollRemindersAsync(poll.Id, groupId, poll.Title,
            new DateOnly(2030, 11, 20), new TimeOnly(21, 0), It.IsAny<CancellationToken>()), Times.Once);
    }
}
