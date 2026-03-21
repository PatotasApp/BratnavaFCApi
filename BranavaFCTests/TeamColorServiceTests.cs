using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using FluentAssertions;

namespace BranavaFC.Tests;

public class TeamColorServiceTests
{
    // ─── GetAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_WhenGroupNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WhenGroupNotFound_ShouldReturnFailure));
        var sut = new TeamColorService(db);

        var result = await sut.GetAllAsync(Guid.NewGuid(), activeOnly: false, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetAllAsync_WhenActiveOnly_ShouldReturnOnlyActiveColors()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WhenActiveOnly_ShouldReturnOnlyActiveColors));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var active   = new TeamColorEntity(group.Id, "Azul",     "#0000FF");
        var inactive = new TeamColorEntity(group.Id, "Vermelho",  "#FF0000");
        inactive.Inactivate();
        db.TeamColors.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var result = await sut.GetAllAsync(group.Id, activeOnly: true, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Should().HaveCount(1);
        result.Data!.Single().Name.Should().Be("Azul");
    }

    [Fact]
    public async Task GetAllAsync_WhenNotActiveOnly_ShouldReturnAllColors()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WhenNotActiveOnly_ShouldReturnAllColors));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var active   = new TeamColorEntity(group.Id, "Azul",    "#0000FF");
        var inactive = new TeamColorEntity(group.Id, "Cinza",   "#888888");
        inactive.Inactivate();
        db.TeamColors.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var result = await sut.GetAllAsync(group.Id, activeOnly: false, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllAsync_ShouldOrderActiveFirst_ThenByName()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_ShouldOrderActiveFirst_ThenByName));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var c1 = new TeamColorEntity(group.Id, "Verde",    "#00FF00");
        var c2 = new TeamColorEntity(group.Id, "Azul",     "#0000FF");
        var c3 = new TeamColorEntity(group.Id, "Cinza",    "#888888");
        c3.Inactivate();
        db.TeamColors.AddRange(c1, c2, c3);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var result = await sut.GetAllAsync(group.Id, activeOnly: false, CancellationToken.None);

        result.Success.Should().BeTrue();
        var names = result.Data!.Select(x => x.Name).ToList();
        // ativos primeiro (Azul, Verde), depois inativo (Cinza)
        names[0].Should().Be("Azul");
        names[1].Should().Be("Verde");
        names[2].Should().Be("Cinza");
    }

    // ─── GetByIdAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_WhenNotFound_ShouldReturnFailure));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var result = await sut.GetByIdAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Cor do time nao encontrada para este grupo.");
    }

    [Fact]
    public async Task GetByIdAsync_WhenExists_ShouldReturnDto()
    {
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_WhenExists_ShouldReturnDto));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var color = new TeamColorEntity(group.Id, "Azul", "#0000FF");
        db.TeamColors.Add(color);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var result = await sut.GetByIdAsync(group.Id, color.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Name.Should().Be("Azul");
        result.Data.HexValue.Should().Be("#0000FF");
    }

    // ─── CreateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WhenDtoNull_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenDtoNull_ShouldReturnFailure));
        var sut = new TeamColorService(db);

        var result = await sut.CreateAsync(null!, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Be("Payload invalido.");
    }

    [Fact]
    public async Task CreateAsync_WhenValid_ShouldPersist()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenValid_ShouldPersist));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var dto = new CreateTeamColorDto
        {
            GroupId  = group.Id,
            Name     = " Azul ",
            HexValue = "1a2b3c"
        };

        var result = await sut.CreateAsync(dto, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.GroupId.Should().Be(group.Id);
        result.Data.Name.Should().Be("Azul");        // trimmed
        result.Data.HexValue.Should().Be("#1A2B3C"); // normalized

        var saved = await db.TeamColors.FindAsync(result.Data.Id);
        saved.Should().NotBeNull();
        saved!.Name.Should().Be("Azul");
    }

    [Fact]
    public async Task CreateAsync_WhenGroupNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenGroupNotFound_ShouldReturnFailure));
        var sut = new TeamColorService(db);

        var dto = new CreateTeamColorDto
        {
            GroupId  = Guid.NewGuid(),
            Name     = "Azul",
            HexValue = "#0000FF"
        };

        var result = await sut.CreateAsync(dto, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // ─── UpdateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WhenNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenNotFound_ShouldReturnFailure));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var result = await sut.UpdateAsync(group.Id, Guid.NewGuid(),
            new UpdateTeamColorDto { Name = "X", HexValue = "#FFFFFF" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Cor do time nao encontrada para este grupo.");
    }

    [Fact]
    public async Task UpdateAsync_WhenDtoNull_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenDtoNull_ShouldReturnFailure));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var result = await sut.UpdateAsync(group.Id, Guid.NewGuid(), null!, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task UpdateAsync_WhenValid_ShouldUpdateNameAndHex()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenValid_ShouldUpdateNameAndHex));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var color = new TeamColorEntity(group.Id, "Azul", "#0000FF");
        db.TeamColors.Add(color);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var result = await sut.UpdateAsync(group.Id, color.Id,
            new UpdateTeamColorDto { Name = "Verde", HexValue = "00FF00" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Name.Should().Be("Verde");
        result.Data.HexValue.Should().Be("#00FF00");

        var saved = await db.TeamColors.FindAsync(color.Id);
        saved!.Name.Should().Be("Verde");
    }

    // ─── InactivateAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task InactivateAsync_WhenNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(InactivateAsync_WhenNotFound_ShouldReturnFailure));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var result = await sut.InactivateAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Cor do time nao encontrada para este grupo.");
    }

    [Fact]
    public async Task InactivateAsync_WhenValid_ShouldSetIsActiveFalse()
    {
        await using var db = DbContextFactory.Create(nameof(InactivateAsync_WhenValid_ShouldSetIsActiveFalse));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var color = new TeamColorEntity(group.Id, "Azul", "#0000FF");
        db.TeamColors.Add(color);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var result = await sut.InactivateAsync(group.Id, color.Id, CancellationToken.None);

        result.Success.Should().BeTrue();

        var saved = await db.TeamColors.FindAsync(color.Id);
        saved!.IsActive.Should().BeFalse();
    }

    // ─── ActivateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ActivateAsync_WhenNotFound_ShouldReturnFailure()
    {
        await using var db = DbContextFactory.Create(nameof(ActivateAsync_WhenNotFound_ShouldReturnFailure));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var result = await sut.ActivateAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Cor do time nao encontrada para este grupo.");
    }

    [Fact]
    public async Task ActivateAsync_WhenInactive_ShouldSetIsActiveTrue()
    {
        await using var db = DbContextFactory.Create(nameof(ActivateAsync_WhenInactive_ShouldSetIsActiveTrue));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var color = new TeamColorEntity(group.Id, "Azul", "#0000FF");
        color.Inactivate();
        db.TeamColors.Add(color);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var result = await sut.ActivateAsync(group.Id, color.Id, CancellationToken.None);

        result.Success.Should().BeTrue();

        var saved = await db.TeamColors.FindAsync(color.Id);
        saved!.IsActive.Should().BeTrue();
    }
}
