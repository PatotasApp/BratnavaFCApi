using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos.Players;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

public class PlayerServiceTests
{
    [Fact]
    public async Task CreateAsync_WhenGroupNotExists_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenGroupNotExists_ShouldThrow));

        var logger = new Mock<ILogger<PlayerService>>();
        var repo = new Mock<IRepositoryBase<PlayerEntity>>();

        var sut = new PlayerService(repo.Object, logger.Object, db);

        var req = new CreatePlayerDto
        (
            Name: "A",
            UserId: Guid.NewGuid(),
            GroupId:     Guid.NewGuid(),
            SkillPoints: 0,
            IsGoalkeeper: false,
            Status: Status.Active
        );

        // Act
        var act = async () => await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("Group does not exist.");
    }

    [Fact]
    public async Task CreateAsync_WhenUserNotExists_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenUserNotExists_ShouldThrow));

        var group = new GroupEntity("G", null);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo = new Mock<IRepositoryBase<PlayerEntity>>();
        var sut = new PlayerService(repo.Object, logger.Object, db);

        var req = new CreatePlayerDto
        (
            Name: "A",
            UserId: Guid.NewGuid(),
            GroupId: group.Id,
            SkillPoints: 0,
            IsGoalkeeper: false,
            Status: Status.Active
        );

        // Act
        var act = async () => await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("User does not exist.");
    }

    [Fact]
    public async Task CreateAsync_WhenPlayerAlreadyExistsInGroup_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenPlayerAlreadyExistsInGroup_ShouldThrow));

        var group = new GroupEntity("G", null);
        var user = new UserEntity("u", "f", "l", "mail@test.com", "hash", null, null);

        db.Groups.Add(group);
        db.Users.Add(user);

        var existing = new PlayerEntity("P", user.Id, group.Id, 0, false, Status.Active);
        db.Players.Add(existing);

        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo = new Mock<IRepositoryBase<PlayerEntity>>();
        var sut = new PlayerService(repo.Object, logger.Object, db);

        var req = new CreatePlayerDto
        (
            Name: "A",
            UserId: user.Id,
            GroupId: group.Id,
            SkillPoints: 0,
            IsGoalkeeper: false,
            Status: Status.Active
        );

        // Act
        var act = async () => await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Player already exists in the group.");
    }

    [Fact]
    public async Task CreateAsync_WhenValid_ShouldAdd_AndSave()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenValid_ShouldAdd_AndSave));

        var group = new GroupEntity("G", null);
        var user = new UserEntity("u", "f", "l", "mail@test.com", "hash", null, null);

        db.Groups.Add(group);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();

        var repo = new RepositoryBase<PlayerEntity>(db);  
        var sut = new PlayerService(repo, logger.Object, db);

        var req = new CreatePlayerDto
        (
            Name: "  Caio  ",
            UserId: user.Id,
            GroupId: group.Id,
            SkillPoints: 10,
            IsGoalkeeper: true,
            Status: Status.Active
        );

        // Act
        var player = await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        player.Should().NotBeNull();
        player.Id.Should().NotBe(Guid.Empty);

        var created = await db.Players.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == player.Id);
        created.Should().NotBeNull();
        created!.Name.Should().Be("Caio");
        created.UserId.Should().Be(user.Id);
        created.GroupId.Should().Be(group.Id);
    }

    [Fact]
    public async Task UpdateAsync_WhenNotFound_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenNotFound_ShouldThrow));

        var logger = new Mock<ILogger<PlayerService>>();
        var repo = new Mock<IRepositoryBase<PlayerEntity>>();

        repo.Setup(r => r.GetByIdIncludingInactiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEntity?)null);

        var sut = new PlayerService(repo.Object, logger.Object, db);

        var req = new UpdatePlayerDto
        (
            Name: "X",
            GroupId: Guid.NewGuid(),
            SkillPoints: 0,
            IsGoalkeeper: false,
            Status: Status.Active
        );

        // Act
        var act = async () => await sut.UpdateAsync(Guid.NewGuid(), req, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("PlayerEntity not found.");
    }
}
