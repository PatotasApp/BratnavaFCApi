using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using FluentAssertions;

namespace BranavaFC.Tests;

public class TeamColorServiceTests
{
    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_WhenNotFound_ShouldThrow));

        var group = new GroupEntity("G", null);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        // Act
        var act = async () => await sut.GetByIdAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cor do time não encontrada para este grupo.");
    }

    [Fact]
    public async Task CreateAsync_WhenDtoNull_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenDtoNull_ShouldThrow));
        var sut = new TeamColorService(db);

        // Act
        var act = async () => await sut.CreateAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Payload inválido.");
    }

    [Fact]
    public async Task CreateAsync_WhenValid_ShouldPersist()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenValid_ShouldPersist));

        var group = new GroupEntity("G", null);
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
        var created = await sut.CreateAsync(dto, CancellationToken.None);

        // Assert
        created.GroupId.Should().Be(group.Id);
        created.Name.Should().Be("Azul");
        created.HexValue.Should().Be("#1A2B3C");

        var saved = await db.TeamColors.FindAsync(created.Id);
        saved.Should().NotBeNull();
        saved!.Name.Should().Be("Azul");
    }

    [Fact]
    public async Task InactivateAsync_WhenNotFound_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(InactivateAsync_WhenNotFound_ShouldThrow));

        var group = new GroupEntity("G", null);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = new TeamColorService(db);

        // Act
        var act = async () => await sut.InactivateAsync(group.Id, Guid.NewGuid(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cor do time não encontrada para este grupo.");
    }
}