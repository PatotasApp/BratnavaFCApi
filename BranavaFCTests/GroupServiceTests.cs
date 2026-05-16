using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Groups;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using BratnavaFC.Domain.Dtos.Players;

namespace BranavaFC.Tests;

public class GroupServiceTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static GroupService CreateSut(BratnavaFC.Infrastructure.Data.AppDbContext db)
    {
        var logger = new Mock<ILogger<GroupService>>();
        var repo   = new RepositoryBase<GroupEntity>(db);
        var push   = Mock.Of<IPushService>();
        return new GroupService(db, logger.Object, repo, push);
    }

    // ─── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WhenAdminUserNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenAdminUserNotFound_ShouldReturnFailure));
        var sut = CreateSut(db);

        var req = new CreateGroupDto("Patota", [Guid.NewGuid()], null, Guid.NewGuid());

        // Act
        var result = await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
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
        var req = new CreateGroupDto("  Minha Patota  ", [user.Id], null, user.Id);

        // Act
        var result = await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Status.Should().Be(ResultStatus.Created);
        result.Data.Should().NotBe(Guid.Empty);

        var groupId = result.Data;
        var saved = await db.Groups
            .IgnoreQueryFilters()
            .Include(g => g.Admins)
            .FirstOrDefaultAsync(g => g.Id == groupId);

        saved.Should().NotBeNull();
        saved!.Name.Should().Be("Minha Patota");
        saved.Admins.Should().HaveCount(1);
        saved.Admins.First().UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreatePlayerForCreator_WhenUserExists()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_ShouldCreatePlayerForCreator_WhenUserExists));

        var user = new UserEntity("jsilva", "João", "Silva", "j@b.com", "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var req = new CreateGroupDto("Patota do João", [user.Id], null, user.Id);

        // Act
        var result = await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        var player = await db.Players.FirstOrDefaultAsync(p => p.UserId == user.Id);
        player.Should().NotBeNull();
        player!.GroupId.Should().Be(result.Data);
        player.Name.Should().Be("João Silva");
        player.IsGuest.Should().BeFalse();
        player.IsGoalkeeper.Should().BeFalse();
        player.SkillPoints.Should().Be(0);
        player.Status.Should().Be(Status.Active);
    }

    [Theory]
    [InlineData("Maria", "Souza", "Maria Souza")]
    [InlineData("Carlos", "Santos", "Carlos Santos")]
    public async Task CreateAsync_ShouldSetPlayerName_FromCreatorFullName(
        string firstName, string lastName, string expectedName)
    {
        // Arrange
        var dbName = $"{nameof(CreateAsync_ShouldSetPlayerName_FromCreatorFullName)}_{firstName}";
        await using var db = DbContextFactory.Create(dbName);

        var user = new UserEntity($"user_{firstName}", firstName, lastName, $"{firstName}@b.com", "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var req = new CreateGroupDto("Patota", [user.Id], null, user.Id);

        // Act
        var result = await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var player = await db.Players.FirstOrDefaultAsync(p => p.UserId == user.Id);
        player!.Name.Should().Be(expectedName);
    }

    // ─── GetByIdAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_WhenGroupNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_WhenGroupNotFound_ShouldReturnFailure));
        var sut = CreateSut(db);

        // Act
        var result = await sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnAdminIds()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_ShouldReturnAdminIds));

        var user = new UserEntity("admin", "A", "B", "a@b.com", "hash", null, null);
        db.Users.Add(user);

        var group = new GroupEntity("G", null, Guid.NewGuid());
        group.SetAdmins([user.Id]);
        db.Groups.Add(group);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetByIdAsync(group.Id, CancellationToken.None);

        // Assert — garante que adminIds é populado (fix do .Include(g => g.Admins))
        result.Success.Should().BeTrue();
        result.Data!.AdminIds.Should().ContainSingle()
            .Which.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldIncludeInactivePlayers()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_ShouldIncludeInactivePlayers));

        var group = new GroupEntity("G", null, Guid.NewGuid());
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
        result.Success.Should().BeTrue();
        result.Data!.Players.Should().HaveCount(2);
        result.Data.Players.Should().Contain(p => p.Name == "Ativo"   && p.Status == Status.Active);
        result.Data.Players.Should().Contain(p => p.Name == "Inativo" && p.Status == Status.Inactive);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnUserName_WhenPlayerHasLinkedUser()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_ShouldReturnUserName_WhenPlayerHasLinkedUser));

        var user = new UserEntity("andreifs", "Andrei", "S", "a@b.com", "hash", null, null);
        db.Users.Add(user);

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var player = new PlayerEntity("Andrei Salvador", user.Id, group.Id, 5, false, false, Status.Active);
        db.Players.Add(player);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetByIdAsync(group.Id, CancellationToken.None);

        // Assert — garante que UserName é populado (fix do .ThenInclude(p => p.User))
        result.Success.Should().BeTrue();
        var dto = result.Data!.Players.Should().ContainSingle().Subject;
        dto.UserName.Should().Be("andreifs");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNullUserName_ForGuestPlayer()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_ShouldReturnNullUserName_ForGuestPlayer));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var guest = new PlayerEntity("Zezinho", null, group.Id, 0, false, true, Status.Active);
        db.Players.Add(guest);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetByIdAsync(group.Id, CancellationToken.None);

        // Assert — convidado sem conta vinculada deve ter UserName nulo
        result.Success.Should().BeTrue();
        var dto = result.Data!.Players.Should().ContainSingle().Subject;
        dto.UserName.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldNotReturnPlayersFromOtherGroups()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetByIdAsync_ShouldNotReturnPlayersFromOtherGroups));

        var group1 = new GroupEntity("G1", null, Guid.NewGuid());
        var group2 = new GroupEntity("G2", null, Guid.NewGuid());
        db.Groups.AddRange(group1, group2);

        db.Players.Add(new PlayerEntity("P1", null, group1.Id, 0, false, true, Status.Active));
        db.Players.Add(new PlayerEntity("P2", null, group2.Id, 0, false, true, Status.Active));

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetByIdAsync(group1.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Players.Should().ContainSingle()
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

        var group1 = new GroupEntity("Patota do Admin", null, admin.Id);
        group1.SetAdmins([admin.Id]);

        var group2 = new GroupEntity("Outra Patota", null, other.Id);
        group2.SetAdmins([other.Id]);

        db.Groups.AddRange(group1, group2);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetByAdminIdAsync(admin.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle()
            .Which.Name.Should().Be("Patota do Admin");
    }

    // ─── GetAllGroupsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllGroupsAsync_ShouldReturnAllGroups_IncludingInactive()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAllGroupsAsync_ShouldReturnAllGroups_IncludingInactive));

        var activeGroup = new GroupEntity("Ativa", null, Guid.NewGuid());
        var inactiveGroup = new GroupEntity("Inativa", null, Guid.NewGuid());
        inactiveGroup.Inactivate();

        db.Groups.AddRange(activeGroup, inactiveGroup);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetAllGroupsAsync(CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data.Should().Contain(g => g.Name == "Ativa"   && g.Status == Status.Active);
        result.Data.Should().Contain(g => g.Name == "Inativa" && g.Status == Status.Inactive);
    }

    [Fact]
    public async Task GetAllGroupsAsync_ShouldIncludePlayersFromEachGroup()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAllGroupsAsync_ShouldIncludePlayersFromEachGroup));

        var group1 = new GroupEntity("G1", null, Guid.NewGuid());
        var group2 = new GroupEntity("G2", null, Guid.NewGuid());
        db.Groups.AddRange(group1, group2);

        db.Players.Add(new PlayerEntity("P1", null, group1.Id, 0, false, true, Status.Active));
        db.Players.Add(new PlayerEntity("P2", null, group1.Id, 0, false, true, Status.Active));
        db.Players.Add(new PlayerEntity("P3", null, group2.Id, 0, false, true, Status.Active));

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetAllGroupsAsync(CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var g1 = result.Data.Should().ContainSingle(g => g.Name == "G1").Subject;
        var g2 = result.Data.Should().ContainSingle(g => g.Name == "G2").Subject;

        g1.Players.Should().HaveCount(2);
        g2.Players.Should().HaveCount(1).And.Contain(p => p.Name == "P3");
    }

    [Fact]
    public async Task GetAllGroupsAsync_ShouldReturnPlayersAlphabeticallyByGroupName()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAllGroupsAsync_ShouldReturnPlayersAlphabeticallyByGroupName));

        db.Groups.AddRange(new GroupEntity("Zebra", null, Guid.NewGuid()), new GroupEntity("Alpha", null, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetAllGroupsAsync(CancellationToken.None);

        // Assert — ordenados por nome
        result.Success.Should().BeTrue();
        result.Data.First().Name.Should().Be("Alpha");
        result.Data.Last().Name.Should().Be("Zebra");
    }

    [Fact]
    public async Task GetAllGroupsAsync_ShouldIncludeInactivePlayersOfGroup()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAllGroupsAsync_ShouldIncludeInactivePlayersOfGroup));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var active   = new PlayerEntity("Ativo",   null, group.Id, 0, false, true, Status.Active);
        var inactive = new PlayerEntity("Inativo", null, group.Id, 0, false, true, Status.Active);
        inactive.Inactivate();

        db.Players.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetAllGroupsAsync(CancellationToken.None);

        // Assert — deve incluir inativos (IgnoreQueryFilters)
        result.Success.Should().BeTrue();
        var g = result.Data.Should().ContainSingle().Subject;
        g.Players.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllGroupsAsync_ShouldReturnUserName_WhenPlayerHasLinkedUser()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAllGroupsAsync_ShouldReturnUserName_WhenPlayerHasLinkedUser));

        var user = new UserEntity("joaofc", "João", "FC", "j@b.com", "hash", null, null);
        db.Users.Add(user);

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var linked = new PlayerEntity("João FC", user.Id, group.Id, 5, false, false, Status.Active);
        var guest  = new PlayerEntity("Guest",   null,    group.Id, 0, false, true,  Status.Active);
        db.Players.AddRange(linked, guest);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.GetAllGroupsAsync(CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var g = result.Data.Should().ContainSingle().Subject;
        g.Players.Should().HaveCount(2);

        var linkedDto = g.Players.Should().ContainSingle(p => p.Name == "João FC").Subject;
        linkedDto.UserName.Should().Be("joaofc");

        var guestDto = g.Players.Should().ContainSingle(p => p.Name == "Guest").Subject;
        guestDto.UserName.Should().BeNull();
    }

    // ─── DeleteAsync (cascade) ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ShouldDeleteGroup_WhenNoRelatedData()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_ShouldDeleteGroup_WhenNoRelatedData));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.DeleteAsync(group.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var exists = await db.Groups.IgnoreQueryFilters().AnyAsync(g => g.Id == group.Id);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeletePlayers_WhenGroupHasPlayers()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_ShouldDeletePlayers_WhenGroupHasPlayers));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var player = new PlayerEntity("P1", null, group.Id, 0, false, true, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.DeleteAsync(group.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var groupExists  = await db.Groups.IgnoreQueryFilters().AnyAsync(g => g.Id == group.Id);
        var playerExists = await db.Players.IgnoreQueryFilters().AnyAsync(p => p.Id == player.Id);

        groupExists.Should().BeFalse();
        playerExists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteTeamColors_WhenGroupHasColors()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_ShouldDeleteTeamColors_WhenGroupHasColors));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var color = new TeamColorEntity(group.Id, "Azul", "#0000FF");
        db.TeamColors.Add(color);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.DeleteAsync(group.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var groupExists = await db.Groups.IgnoreQueryFilters().AnyAsync(g => g.Id == group.Id);
        var colorExists = await db.TeamColors.IgnoreQueryFilters().AnyAsync(c => c.Id == color.Id);

        groupExists.Should().BeFalse();
        colorExists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_WhenGroupNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(DeleteAsync_WhenGroupNotFound_ShouldReturnFailure));
        var sut = CreateSut(db);

        // Act
        var result = await sut.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // ─── AddAdminToGroupAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task AddAdminToGroupAsync_WhenUserNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(AddAdminToGroupAsync_WhenUserNotFound_ShouldReturnFailure));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.AddAdminToGroupAsync(
            group.Id,
            new AddAdminToGroupDto(Guid.NewGuid()),
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task AddAdminToGroupAsync_WhenGroupNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(AddAdminToGroupAsync_WhenGroupNotFound_ShouldReturnFailure));

        var user = new UserEntity("u", "F", "L", "u@b.com", "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.AddAdminToGroupAsync(
            Guid.NewGuid(),
            new AddAdminToGroupDto(user.Id),
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task AddAdminToGroupAsync_WhenAlreadyAdmin_ShouldNotDuplicate()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(AddAdminToGroupAsync_WhenAlreadyAdmin_ShouldNotDuplicate));

        var user = new UserEntity("u", "F", "L", "u@b.com", "hash", null, null);
        db.Users.Add(user);

        var group = new GroupEntity("G", null, Guid.NewGuid());
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

        var group = new GroupEntity("G", null, Guid.NewGuid());
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

    // ─── RemoveAdminAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAdminAsync_WhenGroupNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(RemoveAdminAsync_WhenGroupNotFound_ShouldReturnFailure));
        var sut = CreateSut(db);

        // Act
        var result = await sut.RemoveAdminAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task RemoveAdminAsync_WhenRequestingUserIsNotAdmin_ShouldReturnForbidden()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(RemoveAdminAsync_WhenRequestingUserIsNotAdmin_ShouldReturnForbidden));

        var creator = new UserEntity("creator", "C", "R", "c@b.com", "hash", null, null);
        var target  = new UserEntity("target",  "T", "G", "t@b.com", "hash", null, null);
        var noAdmin = new UserEntity("noadmin", "N", "A", "n@b.com", "hash", null, null);
        db.Users.AddRange(creator, target, noAdmin);

        var group = new GroupEntity("G", null, creator.Id);
        group.SetAdmins([creator.Id, target.Id]);
        db.Groups.Add(group);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act — noAdmin tenta remover target
        var result = await sut.RemoveAdminAsync(
            group.Id, target.Id, noAdmin.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Forbidden);
    }

    [Fact]
    public async Task RemoveAdminAsync_WhenTargetIsCreator_ShouldThrowInvalidOperationException()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(RemoveAdminAsync_WhenTargetIsCreator_ShouldThrowInvalidOperationException));

        var creator = new UserEntity("creator", "C", "R", "c@b.com", "hash", null, null);
        var admin   = new UserEntity("admin",   "A", "D", "a@b.com", "hash", null, null);
        db.Users.AddRange(creator, admin);

        var group = new GroupEntity("G", null, creator.Id);
        group.SetAdmins([creator.Id, admin.Id]);
        db.Groups.Add(group);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act — admin tenta remover o próprio criador
        var act = async () => await sut.RemoveAdminAsync(
            group.Id, creator.Id, admin.Id, CancellationToken.None);

        // Assert — GroupEntity.RemoveAdmin deve rejeitar (entity-level throw, not service-level)
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The group creator cannot be removed from admins.");
    }

    [Fact]
    public async Task RemoveAdminAsync_WhenValid_ShouldRemoveAdminAndPersist()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(RemoveAdminAsync_WhenValid_ShouldRemoveAdminAndPersist));

        var creator = new UserEntity("creator", "C", "R", "c@b.com", "hash", null, null);
        var target  = new UserEntity("target",  "T", "G", "t@b.com", "hash", null, null);
        db.Users.AddRange(creator, target);

        var group = new GroupEntity("G", null, creator.Id);
        group.SetAdmins([creator.Id, target.Id]);
        db.Groups.Add(group);

        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act — creator remove target
        var result = await sut.RemoveAdminAsync(group.Id, target.Id, creator.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var saved = await db.Groups
            .Include(g => g.Admins)
            .FirstAsync(g => g.Id == group.Id);

        saved.Admins.Should().ContainSingle()
            .Which.UserId.Should().Be(creator.Id);
    }
}
