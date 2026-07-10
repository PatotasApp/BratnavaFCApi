using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Calendar;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BranavaFC.Tests;

public class CalendarServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mock<IHolidayService> MakeHolidayMock(params HolidayDto[] holidays)
    {
        var mock = new Mock<IHolidayService>();
        mock.Setup(h => h.GetHolidaysAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<HolidayDto>>.Ok(holidays));
        return mock;
    }

    private static CalendarService MakeSut(
        AppDbContext db,
        IHolidayService? holidays = null,
        IPushService? push = null,
        INotificationScheduler? scheduler = null)
    {
        return new CalendarService(
            db,
            holidays ?? MakeHolidayMock().Object,
            push ?? Mock.Of<IPushService>(),
            scheduler ?? Mock.Of<INotificationScheduler>());
    }

    private static async Task<GroupEntity> SeedGroupAsync(AppDbContext db)
    {
        var group = new GroupEntity("Grupo Teste", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetEventsAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEventsAsync_WhenGroupIdEmpty_ShouldReturnBadRequest()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_WhenGroupIdEmpty_ShouldReturnBadRequest));
        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(Guid.Empty, new DateOnly(2030, 1, 1), new DateOnly(2030, 1, 31));

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Contain("GroupId inválido");
    }

    [Fact]
    public async Task GetEventsAsync_WhenGroupNotFound_ShouldReturnNotFound()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_WhenGroupNotFound_ShouldReturnNotFound));
        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(Guid.NewGuid(), new DateOnly(2030, 1, 1), new DateOnly(2030, 1, 31));

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Contain("Grupo não encontrado");
    }

    [Fact]
    public async Task GetEventsAsync_WhenStartAfterEnd_ShouldReturnBadRequest()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_WhenStartAfterEnd_ShouldReturnBadRequest));
        var group = await SeedGroupAsync(db);
        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 2, 1), new DateOnly(2030, 1, 1));

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Contain("data inicial");
    }

    [Fact]
    public async Task GetEventsAsync_WhenNoData_ShouldReturnEmptyList()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_WhenNoData_ShouldReturnEmptyList));
        var group = await SeedGroupAsync(db);
        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 1, 1), new DateOnly(2030, 1, 31));

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEventsAsync_ShouldReturnManualEventWithCategoryInfo()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_ShouldReturnManualEventWithCategoryInfo));
        var group = await SeedGroupAsync(db);
        var category = new CalendarCategoryEntity(group.Id, "Churrasco", "#ff0000", "🍖");
        db.CalendarCategories.Add(category);
        var ev = new CalendarEventEntity(
            group.Id, "Confraternização", "Fim de ano",
            category.Id, new DateOnly(2030, 3, 10), new TimeOnly(19, 30),
            timeTbd: false, createdByUserId: Guid.NewGuid(), icon: "🎉");
        db.CalendarEvents.Add(ev);
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 3, 1), new DateOnly(2030, 3, 31));

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        var dto = result.Data![0];
        dto.Type.Should().Be("manual");
        dto.Title.Should().Be("Confraternização");
        dto.Date.Should().Be("2030-03-10");
        dto.Time.Should().Be("19:30");
        dto.TimeTBD.Should().BeFalse();
        dto.CategoryId.Should().Be(category.Id);
        dto.CategoryName.Should().Be("Churrasco");
        dto.CategoryColor.Should().Be("#ff0000");
        dto.CategoryIcon.Should().Be("🍖");
        dto.Icon.Should().Be("🎉");
        dto.Description.Should().Be("Fim de ano");
    }

    [Fact]
    public async Task GetEventsAsync_ManualEventWithoutTimeAndCategory_ShouldMapNulls()
    {
        // Arrange — evento com horário em aberto e sem categoria
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_ManualEventWithoutTimeAndCategory_ShouldMapNulls));
        var group = await SeedGroupAsync(db);
        var ev = new CalendarEventEntity(
            group.Id, "Reunião", null,
            null, new DateOnly(2030, 4, 5), null,
            timeTbd: true, createdByUserId: null);
        db.CalendarEvents.Add(ev);
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 4, 1), new DateOnly(2030, 4, 30));

        // Assert
        result.Success.Should().BeTrue();
        var dto = result.Data!.Single();
        dto.Time.Should().BeNull();
        dto.TimeTBD.Should().BeTrue();
        dto.CategoryId.Should().BeNull();
        dto.CategoryName.Should().BeNull();
        dto.CategoryColor.Should().BeNull();
        dto.CategoryIcon.Should().BeNull();
    }

    [Fact]
    public async Task GetEventsAsync_EventOutsideRange_ShouldNotBeReturned()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_EventOutsideRange_ShouldNotBeReturned));
        var group = await SeedGroupAsync(db);
        db.CalendarEvents.Add(new CalendarEventEntity(
            group.Id, "Fora do range", null, null,
            new DateOnly(2030, 5, 1), null, false, null));
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 6, 1), new DateOnly(2030, 6, 30));

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEventsAsync_ShouldIncludeBirthdaysOfActivePlayersInRange()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_ShouldIncludeBirthdaysOfActivePlayersInRange));
        var group = await SeedGroupAsync(db);

        var user = new UserEntity("joao", "João", "Silva", "joao@test.com", "hash",
            null, new DateTimeOffset(1990, 5, 15, 0, 0, 0, TimeSpan.Zero));
        db.Users.Add(user);
        db.Players.Add(new PlayerEntity("João", user.Id, group.Id, 5, isGoalkeeper: false));
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 5, 1), new DateOnly(2030, 5, 31));

        // Assert
        result.Success.Should().BeTrue();
        var dto = result.Data!.Single();
        dto.Type.Should().Be("birthday");
        dto.Title.Should().Be("João");
        dto.Date.Should().Be("2030-05-15");
        dto.TimeTBD.Should().BeTrue();
        dto.CategoryName.Should().Be("Aniversário");
        dto.CategoryColor.Should().Be("#ec4899");
        dto.CategoryIcon.Should().Be("🎂");
        dto.SourceId.Should().NotBeNull();
    }

    [Fact]
    public async Task GetEventsAsync_BirthdayOutsideRange_ShouldBeExcluded()
    {
        // Arrange — aniversário em maio, range em junho
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_BirthdayOutsideRange_ShouldBeExcluded));
        var group = await SeedGroupAsync(db);

        var user = new UserEntity("maria", "Maria", "Souza", "maria@test.com", "hash",
            null, new DateTimeOffset(1985, 5, 20, 0, 0, 0, TimeSpan.Zero));
        db.Users.Add(user);
        db.Players.Add(new PlayerEntity("Maria", user.Id, group.Id, 5, isGoalkeeper: false));
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 6, 1), new DateOnly(2030, 6, 30));

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEventsAsync_GuestOrInactivePlayers_ShouldNotGenerateBirthdays()
    {
        // Arrange — convidado e jogador sem usuário não geram aniversários
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_GuestOrInactivePlayers_ShouldNotGenerateBirthdays));
        var group = await SeedGroupAsync(db);

        var guestUser = new UserEntity("guest", "Guest", "User", "guest@test.com", "hash",
            null, new DateTimeOffset(1990, 7, 10, 0, 0, 0, TimeSpan.Zero));
        db.Users.Add(guestUser);
        db.Players.Add(new PlayerEntity("Convidado", guestUser.Id, group.Id, 5, isGoalkeeper: false, isGuest: true));
        db.Players.Add(new PlayerEntity("Sem usuário", null, group.Id, 5, isGoalkeeper: false));
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 7, 1), new DateOnly(2030, 7, 31));

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEventsAsync_Feb29Birthday_ShouldSkipNonLeapYearAndAppearOnLeapYear()
    {
        // Arrange — nascido em 29/02: só aparece em anos bissextos
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_Feb29Birthday_ShouldSkipNonLeapYearAndAppearOnLeapYear));
        var group = await SeedGroupAsync(db);

        var user = new UserEntity("leap", "Leap", "Year", "leap@test.com", "hash",
            null, new DateTimeOffset(2000, 2, 29, 0, 0, 0, TimeSpan.Zero));
        db.Users.Add(user);
        db.Players.Add(new PlayerEntity("Bissexto", user.Id, group.Id, 5, isGoalkeeper: false));
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act — 2030 não é bissexto; 2032 é
        var nonLeap = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 2, 1), new DateOnly(2030, 3, 1));
        var leap    = await sut.GetEventsAsync(group.Id, new DateOnly(2032, 2, 1), new DateOnly(2032, 3, 1));

        // Assert
        nonLeap.Data.Should().BeEmpty();
        leap.Data.Should().ContainSingle(e => e.Type == "birthday" && e.Date == "2032-02-29");
    }

    [Fact]
    public async Task GetEventsAsync_RangeSpanningTwoYears_ShouldGenerateBirthdayPerYear()
    {
        // Arrange — range dez/2030 a jan/2031 com aniversário em janeiro
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_RangeSpanningTwoYears_ShouldGenerateBirthdayPerYear));
        var group = await SeedGroupAsync(db);

        var user = new UserEntity("jan", "Jan", "Eiro", "jan@test.com", "hash",
            null, new DateTimeOffset(1995, 1, 10, 0, 0, 0, TimeSpan.Zero));
        db.Users.Add(user);
        db.Players.Add(new PlayerEntity("Janeiro", user.Id, group.Id, 5, isGoalkeeper: false));
        await db.SaveChangesAsync();

        var holidayMock = MakeHolidayMock();
        var sut = MakeSut(db, holidays: holidayMock.Object);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 12, 1), new DateOnly(2031, 1, 31));

        // Assert — apenas o aniversário de 2031 cai no range; feriados consultados para os dois anos
        result.Data.Should().ContainSingle(e => e.Type == "birthday" && e.Date == "2031-01-10");
        holidayMock.Verify(h => h.GetHolidaysAsync(2030, It.IsAny<CancellationToken>()), Times.Once);
        holidayMock.Verify(h => h.GetHolidaysAsync(2031, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEventsAsync_ShouldKeepMatchWallClockTime()
    {
        // Arrange — match times are displayed as saved, without timezone conversion.
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_ShouldKeepMatchWallClockTime));
        var group = await SeedGroupAsync(db);
        var playedAt = DateTime.SpecifyKind(new DateTime(2030, 5, 10, 15, 0, 0), DateTimeKind.Utc);
        var match = new MatchEntity(group.Id, playedAt, "Arena Bratnava");
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 5, 1), new DateOnly(2030, 5, 31));

        // Assert
        result.Success.Should().BeTrue();
        var dto = result.Data!.Single();
        dto.Type.Should().Be("match");
        dto.Title.Should().Be("Arena Bratnava");
        dto.Date.Should().Be("2030-05-10");
        dto.Time.Should().Be("15:00");
        dto.TimeTBD.Should().BeFalse();
        dto.CategoryName.Should().Be("Jogo");
        dto.CategoryIcon.Should().Be("⚽");
        dto.SourceId.Should().Be(match.Id);
    }

    [Fact]
    public async Task GetEventsAsync_MatchWithEmptyPlaceName_ShouldUseDefaultTitle()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_MatchWithEmptyPlaceName_ShouldUseDefaultTitle));
        var group = await SeedGroupAsync(db);
        var playedAt = DateTime.SpecifyKind(new DateTime(2030, 5, 10, 15, 0, 0), DateTimeKind.Utc);
        db.Matches.Add(new MatchEntity(group.Id, playedAt, ""));
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 5, 1), new DateOnly(2030, 5, 31));

        // Assert
        result.Data!.Single().Title.Should().Be("Jogo");
    }

    [Fact]
    public async Task GetEventsAsync_MatchInExpandedUtcWindowButLocalDateOutOfRange_ShouldBeExcluded()
    {
        // Arrange — 2030-05-11 03:30 UTC = 2030-05-11 00:30 local, fora do range que termina em 10/05,
        // mas dentro da janela UTC expandida (+4h)
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_MatchInExpandedUtcWindowButLocalDateOutOfRange_ShouldBeExcluded));
        var group = await SeedGroupAsync(db);
        var playedAt = DateTime.SpecifyKind(new DateTime(2030, 5, 11, 3, 30, 0), DateTimeKind.Utc);
        db.Matches.Add(new MatchEntity(group.Id, playedAt, "Arena"));
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 5, 1), new DateOnly(2030, 5, 10));

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEventsAsync_ShouldIncludePollEventsOfTypeEvent()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_ShouldIncludePollEventsOfTypeEvent));
        var group = await SeedGroupAsync(db);

        var eventPoll = new PollEntity(group.Id, "Churrasco de fim de ano", "Traga bebida",
            allowMultipleVotes: false, showVotes: false, createdByUserId: Guid.NewGuid(),
            type: "event", eventDate: new DateOnly(2030, 8, 20), eventTime: new TimeOnly(18, 0),
            eventLocation: "Sede", eventIcon: "🍺");
        var normalPoll = new PollEntity(group.Id, "Votação comum", null,
            allowMultipleVotes: false, showVotes: false, createdByUserId: Guid.NewGuid(),
            type: "poll", eventDate: new DateOnly(2030, 8, 21));
        db.Polls.AddRange(eventPoll, normalPoll);
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 8, 1), new DateOnly(2030, 8, 31));

        // Assert — apenas a poll do tipo "event" vira evento de calendário
        result.Success.Should().BeTrue();
        var dto = result.Data!.Single();
        dto.Type.Should().Be("event");
        dto.Title.Should().Be("Churrasco de fim de ano");
        dto.Date.Should().Be("2030-08-20");
        dto.Time.Should().Be("18:00");
        dto.CategoryName.Should().Be("Evento");
        dto.CategoryIcon.Should().Be("🍺");
        dto.Icon.Should().Be("🍺");
        dto.Description.Should().Be("Traga bebida");
        dto.SourceId.Should().Be(eventPoll.Id);
    }

    [Fact]
    public async Task GetEventsAsync_PollEventWithoutIconOrTime_ShouldUseDefaults()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_PollEventWithoutIconOrTime_ShouldUseDefaults));
        var group = await SeedGroupAsync(db);
        db.Polls.Add(new PollEntity(group.Id, "Festa", null,
            allowMultipleVotes: false, showVotes: false, createdByUserId: null,
            type: "event", eventDate: new DateOnly(2030, 9, 5)));
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 9, 1), new DateOnly(2030, 9, 30));

        // Assert
        var dto = result.Data!.Single();
        dto.Time.Should().BeNull();
        dto.CategoryIcon.Should().Be("🍖");
        dto.Icon.Should().BeNull();
    }

    [Fact]
    public async Task GetEventsAsync_ShouldIncludeHolidaysInRangeAndSkipInvalidOrOutOfRange()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_ShouldIncludeHolidaysInRangeAndSkipInvalidOrOutOfRange));
        var group = await SeedGroupAsync(db);

        var holidayMock = MakeHolidayMock(
            new HolidayDto("2030-10-12", "Nossa Senhora Aparecida"),
            new HolidayDto("2030-12-25", "Natal"),          // fora do range
            new HolidayDto("data-inválida", "Quebrado"));   // não parseável
        var sut = MakeSut(db, holidays: holidayMock.Object);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 10, 1), new DateOnly(2030, 10, 31));

        // Assert
        result.Success.Should().BeTrue();
        var dto = result.Data!.Single();
        dto.Type.Should().Be("holiday");
        dto.Title.Should().Be("Nossa Senhora Aparecida");
        dto.Date.Should().Be("2030-10-12");
        dto.TimeTBD.Should().BeTrue();
        dto.CategoryName.Should().Be("Feriado");
        dto.CategoryColor.Should().Be("#f59e0b");
    }

    [Fact]
    public async Task GetEventsAsync_JuneOf2026_ShouldIncludeWorldCupMatches()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_JuneOf2026_ShouldIncludeWorldCupMatches));
        var group = await SeedGroupAsync(db);
        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        // Assert
        result.Success.Should().BeTrue();
        var worldCup = result.Data!.Where(e => e.Type == "worldcup").ToList();
        worldCup.Should().HaveCount(3);
        worldCup.Should().Contain(e => e.Title == "Brasil x Marrocos" && e.Date == "2026-06-13" && e.Time == "19:00");
        worldCup.Should().Contain(e => e.Title == "Brasil x Haiti" && e.Date == "2026-06-19" && e.Time == "21:30");
        worldCup.Should().Contain(e => e.Title == "Brasil x Escócia" && e.Date == "2026-06-24" && e.Time == "19:00");
        worldCup.Should().OnlyContain(e => e.CategoryName == "Copa do Mundo 2026");
    }

    [Fact]
    public async Task GetEventsAsync_RangeOutsideJune2026_ShouldNotIncludeWorldCupMatches()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_RangeOutsideJune2026_ShouldNotIncludeWorldCupMatches));
        var group = await SeedGroupAsync(db);
        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        result.Data.Should().NotContain(e => e.Type == "worldcup");
    }

    [Fact]
    public async Task GetEventsAsync_ShouldSortByDateThenTimeThenTitle_WithTbdLast()
    {
        // Arrange — mesmos dias com horários diferentes, TBD e empate de horário
        await using var db = DbContextFactory.Create(nameof(GetEventsAsync_ShouldSortByDateThenTimeThenTitle_WithTbdLast));
        var group = await SeedGroupAsync(db);
        var date = new DateOnly(2030, 11, 10);

        db.CalendarEvents.AddRange(
            new CalendarEventEntity(group.Id, "Zebra", null, null, date, new TimeOnly(10, 0), false, null),
            new CalendarEventEntity(group.Id, "Almoço", null, null, date, new TimeOnly(10, 0), false, null),
            new CalendarEventEntity(group.Id, "Sem hora", null, null, date, null, true, null),
            new CalendarEventEntity(group.Id, "Cedo", null, null, date, new TimeOnly(8, 0), false, null),
            new CalendarEventEntity(group.Id, "Dia seguinte", null, null, date.AddDays(1), new TimeOnly(7, 0), false, null));
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetEventsAsync(group.Id, new DateOnly(2030, 11, 1), new DateOnly(2030, 11, 30));

        // Assert
        result.Data!.Select(e => e.Title).Should().ContainInOrder(
            "Cedo", "Almoço", "Zebra", "Sem hora", "Dia seguinte");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CreateEventAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateEventAsync_WhenGroupNotFound_ShouldReturnNotFound()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateEventAsync_WhenGroupNotFound_ShouldReturnNotFound));
        var sut = MakeSut(db);
        var dto = new CreateCalendarEventDto { Title = "Evento", Date = "2030-01-01" };

        // Act
        var result = await sut.CreateEventAsync(Guid.NewGuid(), Guid.NewGuid(), dto);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task CreateEventAsync_WhenDateInvalid_ShouldReturnBadRequest()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateEventAsync_WhenDateInvalid_ShouldReturnBadRequest));
        var group = await SeedGroupAsync(db);
        var sut = MakeSut(db);
        var dto = new CreateCalendarEventDto { Title = "Evento", Date = "não-é-data" };

        // Act
        var result = await sut.CreateEventAsync(group.Id, Guid.NewGuid(), dto);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Contain("Data inválida");
    }

    [Fact]
    public async Task CreateEventAsync_WhenTimeInvalid_ShouldReturnBadRequest()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateEventAsync_WhenTimeInvalid_ShouldReturnBadRequest));
        var group = await SeedGroupAsync(db);
        var sut = MakeSut(db);
        var dto = new CreateCalendarEventDto { Title = "Evento", Date = "2030-01-01", Time = "25:99" };

        // Act
        var result = await sut.CreateEventAsync(group.Id, Guid.NewGuid(), dto);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Contain("Horário inválido");
    }

    [Fact]
    public async Task CreateEventAsync_WhenCategoryNotFound_ShouldReturnNotFound()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateEventAsync_WhenCategoryNotFound_ShouldReturnNotFound));
        var group = await SeedGroupAsync(db);
        var sut = MakeSut(db);
        var dto = new CreateCalendarEventDto
        {
            Title = "Evento", Date = "2030-01-01", CategoryId = Guid.NewGuid()
        };

        // Act
        var result = await sut.CreateEventAsync(group.Id, Guid.NewGuid(), dto);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Contain("Categoria não encontrada");
    }

    [Fact]
    public async Task CreateEventAsync_WithTimeAndCategory_ShouldPersistAndNotifyAndSchedule()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateEventAsync_WithTimeAndCategory_ShouldPersistAndNotifyAndSchedule));
        var group = await SeedGroupAsync(db);
        var category = new CalendarCategoryEntity(group.Id, "Jogo", "#22c55e", "⚽");
        db.CalendarCategories.Add(category);
        await db.SaveChangesAsync();

        var pushMock = new Mock<IPushService>();
        var schedulerMock = new Mock<INotificationScheduler>();
        var sut = MakeSut(db, push: pushMock.Object, scheduler: schedulerMock.Object);
        var userId = Guid.NewGuid();
        var dto = new CreateCalendarEventDto
        {
            Title = "Amistoso",
            Description = "Contra o time B",
            Date = "2030-07-20",
            Time = "19:30",
            CategoryId = category.Id,
            Icon = "🏆"
        };

        // Act
        var result = await sut.CreateEventAsync(group.Id, userId, dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Status.Should().Be(ResultStatus.Created);
        result.Data!.Title.Should().Be("Amistoso");
        result.Data.Date.Should().Be("2030-07-20");
        result.Data.Time.Should().Be("19:30");
        result.Data.TimeTBD.Should().BeFalse();
        result.Data.CategoryName.Should().Be("Jogo");
        result.Data.Icon.Should().Be("🏆");

        var saved = await db.CalendarEvents.SingleAsync();
        saved.GroupId.Should().Be(group.Id);
        saved.CreatedByUserId.Should().Be(userId);
        saved.EventTime.Should().Be(new TimeOnly(19, 30));

        // Notificação com horário no corpo + agendamento de lembretes
        pushMock.Verify(p => p.SendToGroupAsync(
            group.Id,
            It.Is<string>(t => t.Contains("Amistoso")),
            It.Is<string>(b => b.Contains("19:30")),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        schedulerMock.Verify(s => s.ScheduleCalendarRemindersAsync(
            saved.Id, group.Id, "Amistoso", new DateOnly(2030, 7, 20), new TimeOnly(19, 30),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateEventAsync_WithTimeTbd_ShouldIgnoreTimeAndNotifyWithoutTime()
    {
        // Arrange — TimeTBD tem prioridade sobre Time informado
        await using var db = DbContextFactory.Create(nameof(CreateEventAsync_WithTimeTbd_ShouldIgnoreTimeAndNotifyWithoutTime));
        var group = await SeedGroupAsync(db);

        var pushMock = new Mock<IPushService>();
        var sut = MakeSut(db, push: pushMock.Object);
        var dto = new CreateCalendarEventDto
        {
            Title = "Sem hora", Date = "2030-07-21", Time = "10:00", TimeTBD = true
        };

        // Act
        var result = await sut.CreateEventAsync(group.Id, Guid.NewGuid(), dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Time.Should().BeNull();
        result.Data.TimeTBD.Should().BeTrue();

        pushMock.Verify(p => p.SendToGroupAsync(
            group.Id,
            It.IsAny<string>(),
            It.Is<string>(b => !b.Contains("às")),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateEventAsync_WithWhitespaceTime_ShouldStoreNullTime()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateEventAsync_WithWhitespaceTime_ShouldStoreNullTime));
        var group = await SeedGroupAsync(db);
        var sut = MakeSut(db);
        var dto = new CreateCalendarEventDto { Title = "Evento", Date = "2030-07-22", Time = "   " };

        // Act
        var result = await sut.CreateEventAsync(group.Id, Guid.NewGuid(), dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Time.Should().BeNull();
        result.Data.TimeTBD.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UpdateEventAsync
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<CalendarEventEntity> SeedEventAsync(
        AppDbContext db, Guid groupId,
        string title = "Original",
        DateOnly? date = null,
        TimeOnly? time = null,
        bool timeTbd = false,
        Guid? categoryId = null)
    {
        var ev = new CalendarEventEntity(
            groupId, title, "desc original", categoryId,
            date ?? new DateOnly(2030, 1, 15), time, timeTbd, Guid.NewGuid());
        db.CalendarEvents.Add(ev);
        await db.SaveChangesAsync();
        return ev;
    }

    [Fact]
    public async Task UpdateEventAsync_WhenEventNotFound_ShouldReturnNotFound()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateEventAsync_WhenEventNotFound_ShouldReturnNotFound));
        var sut = MakeSut(db);

        // Act
        var result = await sut.UpdateEventAsync(Guid.NewGuid(), Guid.NewGuid(), new UpdateCalendarEventDto());

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Contain("Evento não encontrado");
    }

    [Fact]
    public async Task UpdateEventAsync_WhenGroupIdMismatch_ShouldReturnNotFound()
    {
        // Arrange — evento existe mas pertence a outro grupo
        await using var db = DbContextFactory.Create(nameof(UpdateEventAsync_WhenGroupIdMismatch_ShouldReturnNotFound));
        var group = await SeedGroupAsync(db);
        var ev = await SeedEventAsync(db, group.Id);
        var sut = MakeSut(db);

        // Act
        var result = await sut.UpdateEventAsync(Guid.NewGuid(), ev.Id, new UpdateCalendarEventDto { Title = "Novo" });

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task UpdateEventAsync_WhenDateInvalid_ShouldReturnBadRequest()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateEventAsync_WhenDateInvalid_ShouldReturnBadRequest));
        var group = await SeedGroupAsync(db);
        var ev = await SeedEventAsync(db, group.Id);
        var sut = MakeSut(db);

        // Act
        var result = await sut.UpdateEventAsync(group.Id, ev.Id, new UpdateCalendarEventDto { Date = "31-12-xx" });

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Contain("Data inválida");
    }

    [Fact]
    public async Task UpdateEventAsync_WhenTimeInvalid_ShouldReturnBadRequest()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateEventAsync_WhenTimeInvalid_ShouldReturnBadRequest));
        var group = await SeedGroupAsync(db);
        var ev = await SeedEventAsync(db, group.Id);
        var sut = MakeSut(db);

        // Act
        var result = await sut.UpdateEventAsync(group.Id, ev.Id, new UpdateCalendarEventDto { Time = "99:99" });

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Contain("Horário inválido");
    }

    [Fact]
    public async Task UpdateEventAsync_WhenCategoryNotFound_ShouldReturnNotFound()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateEventAsync_WhenCategoryNotFound_ShouldReturnNotFound));
        var group = await SeedGroupAsync(db);
        var ev = await SeedEventAsync(db, group.Id);
        var sut = MakeSut(db);

        // Act
        var result = await sut.UpdateEventAsync(group.Id, ev.Id,
            new UpdateCalendarEventDto { CategoryId = Guid.NewGuid() });

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Contain("Categoria não encontrada");
    }

    [Fact]
    public async Task UpdateEventAsync_ShouldUpdateFieldsAndReschedule()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateEventAsync_ShouldUpdateFieldsAndReschedule));
        var group = await SeedGroupAsync(db);
        var category = new CalendarCategoryEntity(group.Id, "Nova categoria", "#0000ff", "📅");
        db.CalendarCategories.Add(category);
        await db.SaveChangesAsync();
        var ev = await SeedEventAsync(db, group.Id, time: new TimeOnly(10, 0));

        var schedulerMock = new Mock<INotificationScheduler>();
        var sut = MakeSut(db, scheduler: schedulerMock.Object);
        var dto = new UpdateCalendarEventDto
        {
            Title = "Atualizado",
            Description = "nova descrição",
            Date = "2030-02-20",
            Time = "16:45",
            CategoryId = category.Id,
            Icon = "🔥"
        };

        // Act
        var result = await sut.UpdateEventAsync(group.Id, ev.Id, dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Title.Should().Be("Atualizado");
        result.Data.Description.Should().Be("nova descrição");
        result.Data.Date.Should().Be("2030-02-20");
        result.Data.Time.Should().Be("16:45");
        result.Data.CategoryId.Should().Be(category.Id);
        result.Data.CategoryName.Should().Be("Nova categoria");
        result.Data.Icon.Should().Be("🔥");

        schedulerMock.Verify(s => s.RescheduleCalendarRemindersAsync(
            ev.Id, group.Id, "Atualizado", new DateOnly(2030, 2, 20), new TimeOnly(16, 45),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateEventAsync_WithNullFields_ShouldKeepExistingValues()
    {
        // Arrange — dto vazio não altera nada
        await using var db = DbContextFactory.Create(nameof(UpdateEventAsync_WithNullFields_ShouldKeepExistingValues));
        var group = await SeedGroupAsync(db);
        var ev = await SeedEventAsync(db, group.Id, title: "Mantém", time: new TimeOnly(9, 0));
        var sut = MakeSut(db);

        // Act
        var result = await sut.UpdateEventAsync(group.Id, ev.Id, new UpdateCalendarEventDto());

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Title.Should().Be("Mantém");
        result.Data.Date.Should().Be("2030-01-15");
        result.Data.Time.Should().Be("09:00");
        result.Data.TimeTBD.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateEventAsync_SettingTimeTbdTrue_ShouldClearTime()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateEventAsync_SettingTimeTbdTrue_ShouldClearTime));
        var group = await SeedGroupAsync(db);
        var ev = await SeedEventAsync(db, group.Id, time: new TimeOnly(14, 0));
        var sut = MakeSut(db);

        // Act
        var result = await sut.UpdateEventAsync(group.Id, ev.Id, new UpdateCalendarEventDto { TimeTBD = true });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Time.Should().BeNull();
        result.Data.TimeTBD.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateEventAsync_SettingTimeTbdFalseWithNewTime_ShouldSetTime()
    {
        // Arrange — evento estava com horário em aberto
        await using var db = DbContextFactory.Create(nameof(UpdateEventAsync_SettingTimeTbdFalseWithNewTime_ShouldSetTime));
        var group = await SeedGroupAsync(db);
        var ev = await SeedEventAsync(db, group.Id, timeTbd: true);
        var sut = MakeSut(db);

        // Act
        var result = await sut.UpdateEventAsync(group.Id, ev.Id,
            new UpdateCalendarEventDto { TimeTBD = false, Time = "20:15" });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Time.Should().Be("20:15");
        result.Data.TimeTBD.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DeleteEventAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteEventAsync_WhenEventNotFound_ShouldReturnOkWithoutNotifying()
    {
        // Arrange — exclusão é idempotente
        await using var db = DbContextFactory.Create(nameof(DeleteEventAsync_WhenEventNotFound_ShouldReturnOkWithoutNotifying));
        var pushMock = new Mock<IPushService>();
        var sut = MakeSut(db, push: pushMock.Object);

        // Act
        var result = await sut.DeleteEventAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        result.Success.Should().BeTrue();
        pushMock.Verify(p => p.SendToGroupAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteEventAsync_WhenFound_ShouldRemoveCancelRemindersAndNotify()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(DeleteEventAsync_WhenFound_ShouldRemoveCancelRemindersAndNotify));
        var group = await SeedGroupAsync(db);
        var ev = await SeedEventAsync(db, group.Id, title: "Para excluir");

        var pushMock = new Mock<IPushService>();
        var schedulerMock = new Mock<INotificationScheduler>();
        var sut = MakeSut(db, push: pushMock.Object, scheduler: schedulerMock.Object);

        // Act
        var result = await sut.DeleteEventAsync(group.Id, ev.Id);

        // Assert
        result.Success.Should().BeTrue();
        (await db.CalendarEvents.AnyAsync()).Should().BeFalse();
        schedulerMock.Verify(s => s.CancelCalendarRemindersAsync(ev.Id, It.IsAny<CancellationToken>()), Times.Once);
        pushMock.Verify(p => p.SendToGroupAsync(
            group.Id,
            "Evento cancelado",
            It.Is<string>(b => b.Contains("Para excluir")),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetCategoriesAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCategoriesAsync_WhenGroupIdEmpty_ShouldReturnBadRequest()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetCategoriesAsync_WhenGroupIdEmpty_ShouldReturnBadRequest));
        var sut = MakeSut(db);

        // Act
        var result = await sut.GetCategoriesAsync(Guid.Empty);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task GetCategoriesAsync_WhenGroupNotFound_ShouldReturnNotFound()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetCategoriesAsync_WhenGroupNotFound_ShouldReturnNotFound));
        var sut = MakeSut(db);

        // Act
        var result = await sut.GetCategoriesAsync(Guid.NewGuid());

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetCategoriesAsync_ShouldReturnCategoriesOrderedByName()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetCategoriesAsync_ShouldReturnCategoriesOrderedByName));
        var group = await SeedGroupAsync(db);
        var otherGroup = await SeedGroupAsync(db);
        db.CalendarCategories.AddRange(
            new CalendarCategoryEntity(group.Id, "Zebra", "#111111", null),
            new CalendarCategoryEntity(group.Id, "Almoço", "#222222", "🍽️", isSystem: true),
            new CalendarCategoryEntity(otherGroup.Id, "De outro grupo", null, null));
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.GetCategoriesAsync(group.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data![0].Name.Should().Be("Almoço");
        result.Data[0].IsSystem.Should().BeTrue();
        result.Data[0].Icon.Should().Be("🍽️");
        result.Data[1].Name.Should().Be("Zebra");
        result.Data[1].IsSystem.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CreateCategoryAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateCategoryAsync_WhenGroupNotFound_ShouldReturnNotFound()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateCategoryAsync_WhenGroupNotFound_ShouldReturnNotFound));
        var sut = MakeSut(db);

        // Act
        var result = await sut.CreateCategoryAsync(Guid.NewGuid(), new CreateCalendarCategoryDto { Name = "X" });

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task CreateCategoryAsync_ShouldPersistAndReturnCreated()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateCategoryAsync_ShouldPersistAndReturnCreated));
        var group = await SeedGroupAsync(db);
        var sut = MakeSut(db);
        var dto = new CreateCalendarCategoryDto { Name = "  Treino  ", Color = "#3b82f6", Icon = "🏃" };

        // Act
        var result = await sut.CreateCategoryAsync(group.Id, dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Status.Should().Be(ResultStatus.Created);
        result.Data!.Name.Should().Be("Treino", "o nome deve ser trimado");
        result.Data.Color.Should().Be("#3b82f6");
        result.Data.Icon.Should().Be("🏃");
        result.Data.IsSystem.Should().BeFalse();

        (await db.CalendarCategories.CountAsync()).Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UpdateCategoryAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCategoryAsync_WhenNotFound_ShouldReturnNotFound()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateCategoryAsync_WhenNotFound_ShouldReturnNotFound));
        var sut = MakeSut(db);

        // Act
        var result = await sut.UpdateCategoryAsync(Guid.NewGuid(), Guid.NewGuid(), new UpdateCalendarCategoryDto());

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Contain("Categoria não encontrada");
    }

    [Fact]
    public async Task UpdateCategoryAsync_WhenGroupIdMismatch_ShouldReturnNotFound()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateCategoryAsync_WhenGroupIdMismatch_ShouldReturnNotFound));
        var group = await SeedGroupAsync(db);
        var category = new CalendarCategoryEntity(group.Id, "Categoria", null, null);
        db.CalendarCategories.Add(category);
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.UpdateCategoryAsync(Guid.NewGuid(), category.Id, new UpdateCalendarCategoryDto { Name = "Novo" });

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task UpdateCategoryAsync_ShouldUpdateAllFields()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateCategoryAsync_ShouldUpdateAllFields));
        var group = await SeedGroupAsync(db);
        var category = new CalendarCategoryEntity(group.Id, "Antiga", "#000000", "📅");
        db.CalendarCategories.Add(category);
        await db.SaveChangesAsync();

        var sut = MakeSut(db);
        var dto = new UpdateCalendarCategoryDto { Name = "Nova", Color = "#ffffff", Icon = "🎯" };

        // Act
        var result = await sut.UpdateCategoryAsync(group.Id, category.Id, dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Name.Should().Be("Nova");
        result.Data.Color.Should().Be("#ffffff");
        result.Data.Icon.Should().Be("🎯");
    }

    [Fact]
    public async Task UpdateCategoryAsync_WithNullFields_ShouldKeepExistingValues()
    {
        // Arrange — dto vazio não altera nada
        await using var db = DbContextFactory.Create(nameof(UpdateCategoryAsync_WithNullFields_ShouldKeepExistingValues));
        var group = await SeedGroupAsync(db);
        var category = new CalendarCategoryEntity(group.Id, "Mantém", "#123456", "🎈");
        db.CalendarCategories.Add(category);
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.UpdateCategoryAsync(group.Id, category.Id, new UpdateCalendarCategoryDto());

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Name.Should().Be("Mantém");
        result.Data.Color.Should().Be("#123456");
        result.Data.Icon.Should().Be("🎈");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DeleteCategoryAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteCategoryAsync_WhenNotFound_ShouldReturnOk()
    {
        // Arrange — exclusão é idempotente
        await using var db = DbContextFactory.Create(nameof(DeleteCategoryAsync_WhenNotFound_ShouldReturnOk));
        var sut = MakeSut(db);

        // Act
        var result = await sut.DeleteCategoryAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteCategoryAsync_WhenSystemCategory_ShouldReturnBadRequest()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(DeleteCategoryAsync_WhenSystemCategory_ShouldReturnBadRequest));
        var group = await SeedGroupAsync(db);
        var category = new CalendarCategoryEntity(group.Id, "Sistema", null, null, isSystem: true);
        db.CalendarCategories.Add(category);
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.DeleteCategoryAsync(group.Id, category.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Contain("sistema");
        (await db.CalendarCategories.CountAsync()).Should().Be(1, "categoria do sistema não deve ser excluída");
    }

    [Fact]
    public async Task DeleteCategoryAsync_WhenRegularCategory_ShouldRemove()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(DeleteCategoryAsync_WhenRegularCategory_ShouldRemove));
        var group = await SeedGroupAsync(db);
        var category = new CalendarCategoryEntity(group.Id, "Comum", null, null);
        db.CalendarCategories.Add(category);
        await db.SaveChangesAsync();

        var sut = MakeSut(db);

        // Act
        var result = await sut.DeleteCategoryAsync(group.Id, category.Id);

        // Assert
        result.Success.Should().BeTrue();
        (await db.CalendarCategories.AnyAsync()).Should().BeFalse();
    }
}
