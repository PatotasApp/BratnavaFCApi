using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using FluentAssertions;

namespace BranavaFC.Tests;

public class TeamColorServiceTests
{
    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_WhenNotFound_ShouldReturnFailure));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        // Act
        var result = await sut.GetByIdAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Cor do time nao encontrada para este grupo.");
    }

    [Fact]
    public async Task CreateAsync_WhenDtoNull_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenDtoNull_ShouldReturnFailure));
        var sut = new TeamColorService(db);

        // Act
        var result = await sut.CreateAsync(null!, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Be("Payload invalido.");
    }

    [Fact]
    public async Task CreateAsync_WhenValid_ShouldPersist()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenValid_ShouldPersist));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        var dto = new CreateTeamColorDto
        {
            GroupId = group.Id,
            Name = " Azul ",
            HexValue = "1a2b3c"
        };

        // Act
        var result = await sut.CreateAsync(dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.GroupId.Should().Be(group.Id);
        result.Data.Name.Should().Be("Azul");
        result.Data.HexValue.Should().Be("#1A2B3C");

        var saved = await db.TeamColors.FindAsync(result.Data.Id);
        saved.Should().NotBeNull();
        saved!.Name.Should().Be("Azul");
    }

    [Fact]
    public async Task InactivateAsync_WhenNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(InactivateAsync_WhenNotFound_ShouldReturnFailure));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        // Act
        var result = await sut.InactivateAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Cor do time nao encontrada para este grupo.");
    }
}
