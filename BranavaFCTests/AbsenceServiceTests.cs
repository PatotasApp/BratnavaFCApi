using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Absences;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;

namespace BranavaFC.Tests;

public class AbsenceServiceTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static AbsenceService CreateSut(BratnavaFC.Infrastructure.Data.AppDbContext db)
        => new(db);

    private static UserEntity MakeUser(string username = "jogador")
        => new(username, username, "Sobrenome", $"{username}@test.com", "hash", null, null);

    private static GroupEntity MakeGroup(Guid createdBy)
        => new("Patota Teste", null, createdBy);

    private static PlayerEntity MakePlayer(string name, Guid? userId, Guid groupId)
        => new(name, userId, groupId, 5m, false);

    private static CreateAbsenceDto ValidDto(
        DateOnly? start = null,
        DateOnly? end   = null,
        AbsenceType type = AbsenceType.Travel,
        string? desc = null)
    {
        var s = start ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var e = end   ?? s.AddDays(3);
        return new CreateAbsenceDto(s, e, type, desc);
    }

    // ─── GetMineAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMineAsync_WhenNoAbsences_ShouldReturnEmptyList()
    {
        await using var db  = DbContextFactory.Create(nameof(GetMineAsync_WhenNoAbsences_ShouldReturnEmptyList));
        var sut = CreateSut(db);

        var result = await sut.GetMineAsync(Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMineAsync_ShouldReturnOnlyAbsencesOfTheGivenUser()
    {
        await using var db = DbContextFactory.Create(nameof(GetMineAsync_ShouldReturnOnlyAbsencesOfTheGivenUser));

        var userA = MakeUser("userA");
        var userB = MakeUser("userB");
        db.Users.AddRange(userA, userB);

        var absA = new UserAbsenceEntity(userA.Id, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 5), AbsenceType.Travel, null);
        var absB = new UserAbsenceEntity(userB.Id, new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 3), AbsenceType.Personal, null);
        db.UserAbsences.AddRange(absA, absB);
        await db.SaveChangesAsync();

        var sut    = CreateSut(db);
        var result = await sut.GetMineAsync(userA.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data[0].Id.Should().Be(absA.Id);
    }

    [Fact]
    public async Task GetMineAsync_ShouldReturnAbsencesOrderedByStartDate()
    {
        await using var db = DbContextFactory.Create(nameof(GetMineAsync_ShouldReturnAbsencesOrderedByStartDate));

        var user = MakeUser();
        db.Users.Add(user);

        var later  = new UserAbsenceEntity(user.Id, new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 3), AbsenceType.Other, null);
        var sooner = new UserAbsenceEntity(user.Id, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 5), AbsenceType.Travel, null);
        db.UserAbsences.AddRange(later, sooner);
        await db.SaveChangesAsync();

        var sut    = CreateSut(db);
        var result = await sut.GetMineAsync(user.Id, CancellationToken.None);

        result.Data.Should().HaveCount(2);
        result.Data[0].StartDate.Should().BeBefore(result.Data[1].StartDate);
    }

    // ─── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WhenValid_ShouldPersistAbsence()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenValid_ShouldPersistAbsence));
        var sut    = CreateSut(db);
        var userId = Guid.NewGuid();

        var dto    = ValidDto(type: AbsenceType.MedicalDepartment, desc: "  consulta  ");
        var result = await sut.CreateAsync(userId, dto, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Status.Should().Be(ResultStatus.Created);
        result.Data.AbsenceTypeName.Should().Be("Departamento Médico");
        result.Data.Description.Should().Be("consulta");

        db.UserAbsences.Should().HaveCount(1);
        db.UserAbsences.First().UserId.Should().Be(userId);
    }

    [Fact]
    public async Task CreateAsync_WhenEndDateBeforeStartDate_ShouldReturnBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenEndDateBeforeStartDate_ShouldReturnBadRequest));
        var sut = CreateSut(db);

        var start  = new DateOnly(2025, 6, 10);
        var dto    = ValidDto(start: start, end: start.AddDays(-1));
        var result = await sut.CreateAsync(Guid.NewGuid(), dto, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        db.UserAbsences.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WhenInvalidAbsenceType_ShouldReturnBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenInvalidAbsenceType_ShouldReturnBadRequest));
        var sut = CreateSut(db);

        var dto    = new CreateAbsenceDto(new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 5), (AbsenceType)99, null);
        var result = await sut.CreateAsync(Guid.NewGuid(), dto, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task CreateAsync_WhenSameDayStartAndEnd_ShouldSucceed()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenSameDayStartAndEnd_ShouldSucceed));
        var sut  = CreateSut(db);
        var date = new DateOnly(2025, 6, 15);

        var result = await sut.CreateAsync(Guid.NewGuid(), ValidDto(start: date, end: date), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.StartDate.Should().Be(date);
        result.Data.EndDate.Should().Be(date);
    }

    // ─── UpdateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WhenValid_ShouldUpdateAbsence()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenValid_ShouldUpdateAbsence));

        var userId  = Guid.NewGuid();
        var absence = new UserAbsenceEntity(userId, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 5), AbsenceType.Travel, "viagem");
        db.UserAbsences.Add(absence);
        await db.SaveChangesAsync();

        var sut    = CreateSut(db);
        var newDto = new CreateAbsenceDto(new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 10), AbsenceType.Personal, "pessoal");
        var result = await sut.UpdateAsync(userId, absence.Id, newDto, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.StartDate.Should().Be(new DateOnly(2025, 7, 1));
        result.Data.AbsenceTypeName.Should().Be("Pessoal");
        result.Data.Description.Should().Be("pessoal");
    }

    [Fact]
    public async Task UpdateAsync_WhenAbsenceBelongsToAnotherUser_ShouldReturnNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenAbsenceBelongsToAnotherUser_ShouldReturnNotFound));

        var ownerUserId   = Guid.NewGuid();
        var requestUserId = Guid.NewGuid();
        var absence = new UserAbsenceEntity(ownerUserId, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 5), AbsenceType.Travel, null);
        db.UserAbsences.Add(absence);
        await db.SaveChangesAsync();

        var sut    = CreateSut(db);
        var result = await sut.UpdateAsync(requestUserId, absence.Id, ValidDto(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task UpdateAsync_WhenEndDateBeforeStartDate_ShouldReturnBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenEndDateBeforeStartDate_ShouldReturnBadRequest));

        var userId  = Guid.NewGuid();
        var absence = new UserAbsenceEntity(userId, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 5), AbsenceType.Travel, null);
        db.UserAbsences.Add(absence);
        await db.SaveChangesAsync();

        var sut    = CreateSut(db);
        var dto    = ValidDto(start: new DateOnly(2025, 8, 10), end: new DateOnly(2025, 8, 5));
        var result = await sut.UpdateAsync(userId, absence.Id, dto, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    // ─── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenAbsenceExists_ShouldRemoveIt()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WhenAbsenceExists_ShouldRemoveIt));

        var userId  = Guid.NewGuid();
        var absence = new UserAbsenceEntity(userId, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 5), AbsenceType.Travel, null);
        db.UserAbsences.Add(absence);
        await db.SaveChangesAsync();

        var sut    = CreateSut(db);
        var result = await sut.DeleteAsync(userId, absence.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        db.UserAbsences.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_WhenAbsenceNotFound_ShouldReturnOkIdempotently()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WhenAbsenceNotFound_ShouldReturnOkIdempotently));
        var sut    = CreateSut(db);
        var result = await sut.DeleteAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_WhenAbsenceBelongsToAnotherUser_ShouldNotDelete()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WhenAbsenceBelongsToAnotherUser_ShouldNotDelete));

        var ownerUserId   = Guid.NewGuid();
        var requestUserId = Guid.NewGuid();
        var absence = new UserAbsenceEntity(ownerUserId, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 5), AbsenceType.Travel, null);
        db.UserAbsences.Add(absence);
        await db.SaveChangesAsync();

        var sut    = CreateSut(db);
        var result = await sut.DeleteAsync(requestUserId, absence.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        db.UserAbsences.Should().HaveCount(1); // não removeu a ausência de outro usuário
    }

    // ─── GetByGroupAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetByGroupAsync_WhenGroupNotFound_ShouldReturnNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(GetByGroupAsync_WhenGroupNotFound_ShouldReturnNotFound));
        var sut    = CreateSut(db);
        var result = await sut.GetByGroupAsync(Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetByGroupAsync_WhenGroupHasNoPlayers_ShouldReturnEmptyList()
    {
        await using var db = DbContextFactory.Create(nameof(GetByGroupAsync_WhenGroupHasNoPlayers_ShouldReturnEmptyList));

        var user  = MakeUser();
        var group = MakeGroup(user.Id);
        db.Users.Add(user);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut    = CreateSut(db);
        var result = await sut.GetByGroupAsync(group.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByGroupAsync_ShouldReturnMembersWithTheirAbsences()
    {
        await using var db = DbContextFactory.Create(nameof(GetByGroupAsync_ShouldReturnMembersWithTheirAbsences));

        var user  = MakeUser();
        var group = MakeGroup(user.Id);
        db.Users.Add(user);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var player = MakePlayer("Ronaldo", user.Id, group.Id);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var absence = new UserAbsenceEntity(user.Id, new DateOnly(2025, 8, 1), new DateOnly(2025, 8, 5), AbsenceType.Travel, "férias");
        db.UserAbsences.Add(absence);
        await db.SaveChangesAsync();

        var sut    = CreateSut(db);
        var result = await sut.GetByGroupAsync(group.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data[0].PlayerName.Should().Be("Ronaldo");
        result.Data[0].Absences.Should().HaveCount(1);
        result.Data[0].Absences[0].Description.Should().Be("férias");
    }

    [Fact]
    public async Task GetByGroupAsync_ShouldIgnoreGuestPlayers()
    {
        await using var db = DbContextFactory.Create(nameof(GetByGroupAsync_ShouldIgnoreGuestPlayers));

        var user  = MakeUser();
        var group = MakeGroup(user.Id);
        db.Users.Add(user);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        // mensalista com userId
        var regularPlayer = MakePlayer("Titular", user.Id, group.Id);
        // convidado sem userId
        var guestPlayer   = new PlayerEntity("Convidado", null, group.Id, 3m, false, isGuest: true);
        db.Players.AddRange(regularPlayer, guestPlayer);
        await db.SaveChangesAsync();

        var sut    = CreateSut(db);
        var result = await sut.GetByGroupAsync(group.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data[0].PlayerName.Should().Be("Titular");
    }

    // ─── GetTypeName ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AbsenceType.Travel,            "Viagem")]
    [InlineData(AbsenceType.MedicalDepartment, "Departamento Médico")]
    [InlineData(AbsenceType.Personal,          "Pessoal")]
    [InlineData(AbsenceType.Other,             "Outros")]
    public void GetTypeName_ShouldReturnCorrectPortugueseName(AbsenceType type, string expected)
    {
        AbsenceService.GetTypeName(type).Should().Be(expected);
    }
}
