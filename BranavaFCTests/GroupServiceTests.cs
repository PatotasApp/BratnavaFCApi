using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos.Groups;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

public class GroupServiceTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static GroupService CreateSut(BratnavaFC.Infrastructure.Data.AppDbContext db)
    {
        var logger = new Mock<ILogger<GroupService>>();
        var repo = new RepositoryBase<GroupEntity>(db);
        return new GroupService(db, logger.Object, repo);
    }

    // ─── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WhenAdminUserNotFound_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenAdminUserNotFound_ShouldThrow));
        var sut = CreateSut(db);

        var req = new CreateGroupDto("Patota", [Guid.NewGuid()], null);

        // Act
        var act = async () => await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("User admin does not exists.");
    }

    [Fact]
    public async Task CreateAsync_WhenValid_ShouldCreateGroup_WithAdmins()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenValid_ShouldCreateGroup_WithAdmins));

        var user = new UserEntity("admin", "A", "B", "a@b.com", "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var req = new CreateGroupDto("  Minha Patota  ", [user.Id], null);

        // Act
        var groupId = await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        groupId.Should().NotBe(Guid.Empty);

        var saved = await db.Groups
            .IgnoreQueryFilters()
            .Include(g => g.Admins)
            .FirstOrDefaultAsync(g => g.Id == groupId);

        saved.Should().NotBeNull();
        saved!.Name.Should().Be("Minha Patota");
        saved.Admins.Should().HaveCount(1);
        saved.Admins.First().UserId.Should().Be(user.Id);
    }

    // ─── GetByIdAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_WhenGroupNotFound_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_WhenGroupNotFound_ShouldThrow));
        var sut = CreateSut(db);

        // Act
        var act = async () => await sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("Group not found.");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnAdminIds()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_ShouldReturnAdminIds));

        var user = new UserEntity("admin", "A", "B", "a@b.com", "hash", null, null);
        db.Users.Add(user);

        var group = new GroupEntity("G", null);
        group.SetAdmins([user.Id]);
        db.Groups.Add(group);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetByIdAsync(group.Id, CancellationToken.None);

        // Assert — garante que adminIds é populado (fix do .Include(g => g.Admins))
        result.AdminIds.Should().ContainSingle()
            .Which.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldIncludeInactivePlayers()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_ShouldIncludeInactivePlayers));

        var group = new GroupEntity("G", null);
        db.Groups.Add(group);

        var activePlayer   = new PlayerEntity("Ativo",   null, group.Id, 0, false, true);
        var inactivePlayer = new PlayerEntity("Inativo", null, group.Id, 0, false, true);
        inactivePlayer.Inactivate();

        db.Players.Add(activePlayer);
        db.Players.Add(inactivePlayer);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetByIdAsync(group.Id, CancellationToken.None);

        // Assert — garante que inativos são retornados (fix do IgnoreQueryFilters)
        result.Players.Should().HaveCount(2);
        result.Players.Should().Contain(p => p.Name == "Ativo"   && p.Status == Status.Active);
        result.Players.Should().Contain(p => p.Name == "Inativo" && p.Status == Status.Inactive);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnUserName_WhenPlayerHasLinkedUser()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_ShouldReturnUserName_WhenPlayerHasLinkedUser));

        var user = new UserEntity("andreifs", "Andrei", "S", "a@b.com", "hash", null, null);
        db.Users.Add(user);

        var group = new GroupEntity("G", null);
        db.Groups.Add(group);

        var player = new PlayerEntity("Andrei Salvador", user.Id, group.Id, 5, false, false, Status.Active);
        db.Players.Add(player);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetByIdAsync(group.Id, CancellationToken.None);

        // Assert — garante que UserName é populado (fix do .ThenInclude(p => p.User))
        var dto = result.Players.Should().ContainSingle().Subject;
        dto.UserName.Should().Be("andreifs");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNullUserName_ForGuestPlayer()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_ShouldReturnNullUserName_ForGuestPlayer));

        var group = new GroupEntity("G", null);
        db.Groups.Add(group);

        var guest = new PlayerEntity("Zezinho", null, group.Id, 0, false, true, Status.Active);
        db.Players.Add(guest);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetByIdAsync(group.Id, CancellationToken.None);

        // Assert — convidado sem conta vinculada deve ter UserName nulo
        var dto = result.Players.Should().ContainSingle().Subject;
        dto.UserName.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldNotReturnPlayersFromOtherGroups()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_ShouldNotReturnPlayersFromOtherGroups));

        var group1 = new GroupEntity("G1", null);
        var group2 = new GroupEntity("G2", null);
        db.Groups.AddRange(group1, group2);

        db.Players.Add(new PlayerEntity("P1", null, group1.Id, 0, false, true, Status.Active));
        db.Players.Add(new PlayerEntity("P2", null, group2.Id, 0, false, true, Status.Active));

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetByIdAsync(group1.Id, CancellationToken.None);

        // Assert
        result.Players.Should().ContainSingle()
            .Which.Name.Should().Be("P1");
    }

    // ─── GetByAdminIdAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetByAdminIdAsync_ShouldReturnOnlyGroupsWhereUserIsAdmin()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByAdminIdAsync_ShouldReturnOnlyGroupsWhereUserIsAdmin));

        var admin = new UserEntity("admin", "A", "B", "a@b.com", "hash", null, null);
        var other = new UserEntity("other", "O", "T", "o@b.com", "hash", null, null);
        db.Users.AddRange(admin, other);

        var group1 = new GroupEntity("Patota do Admin", null);
        group1.SetAdmins([admin.Id]);

        var group2 = new GroupEntity("Outra Patota", null);
        group2.SetAdmins([other.Id]);

        db.Groups.AddRange(group1, group2);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetByAdminIdAsync(admin.Id, CancellationToken.None);

        // Assert
        result.Should().ContainSingle()
            .Which.Name.Should().Be("Patota do Admin");
    }

    // ─── AddAdminToGroupAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task AddAdminToGroupAsync_WhenUserNotFound_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(AddAdminToGroupAsync_WhenUserNotFound_ShouldThrow));

        var group = new GroupEntity("G", null);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var act = async () => await sut.AddAdminToGroupAsync(
            group.Id,
            new AddAdminToGroupDto(Guid.NewGuid()),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("User admin does not exists.");
    }

    [Fact]
    public async Task AddAdminToGroupAsync_WhenGroupNotFound_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(AddAdminToGroupAsync_WhenGroupNotFound_ShouldThrow));

        var user = new UserEntity("u", "F", "L", "u@b.com", "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var act = async () => await sut.AddAdminToGroupAsync(
            Guid.NewGuid(),
            new AddAdminToGroupDto(user.Id),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("Group not found.");
    }

    [Fact]
    public async Task AddAdminToGroupAsync_WhenAlreadyAdmin_ShouldNotDuplicate()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(AddAdminToGroupAsync_WhenAlreadyAdmin_ShouldNotDuplicate));

        var user = new UserEntity("u", "F", "L", "u@b.com", "hash", null, null);
        db.Users.Add(user);

        var group = new GroupEntity("G", null);
        group.SetAdmins([user.Id]);
        db.Groups.Add(group);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act — adiciona o mesmo admin duas vezes
        await sut.AddAdminToGroupAsync(group.Id, new AddAdminToGroupDto(user.Id), CancellationToken.None);

        // Assert — não deve duplicar
        var saved = await db.Groups
            .Include(g => g.Admins)
            .FirstAsync(g => g.Id == group.Id);

        saved.Admins.Should().ContainSingle()
            .Which.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task AddAdminToGroupAsync_WhenValid_ShouldAddAdmin()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(AddAdminToGroupAsync_WhenValid_ShouldAddAdmin));

        var existingAdmin = new UserEntity("admin1", "A", "B", "a@b.com", "hash", null, null);
        var newAdmin      = new UserEntity("admin2", "C", "D", "c@b.com", "hash", null, null);
        db.Users.AddRange(existingAdmin, newAdmin);

        var group = new GroupEntity("G", null);
        group.SetAdmins([existingAdmin.Id]);
        db.Groups.Add(group);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        await sut.AddAdminToGroupAsync(group.Id, new AddAdminToGroupDto(newAdmin.Id), CancellationToken.None);

        // Assert
        var saved = await db.Groups
            .Include(g => g.Admins)
            .FirstAsync(g => g.Id == group.Id);

        saved.Admins.Should().HaveCount(2);
        saved.Admins.Select(a => a.UserId).Should().Contain(newAdmin.Id);
    }
}
