using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
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
    /// <summary>
    /// Returns a pre-configured IMatchService mock that satisfies the sync call
    /// without performing any real DB work. Tests that care about sync behavior
    /// should set up their own mock.
    /// </summary>
    private static IMatchService MatchServiceMock()
    {
        var mock = new Mock<IMatchService>();
        mock.Setup(m => m.SyncPlayerIntoActiveMatchesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        return mock.Object;
    }

    [Fact]
    public async Task CreateAsync_WhenGroupNotExists_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenGroupNotExists_ShouldReturnFailure));

        var logger = new Mock<ILogger<PlayerService>>();
        var repo = new Mock<IRepositoryBase<PlayerEntity>>();

        var sut = new PlayerService(repo.Object, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        var req = new CreatePlayerDto(
            Name: "A",
            UserId: Guid.NewGuid(),
            GroupId: Guid.NewGuid(),
            SkillPoints: 0,
            IsGoalkeeper: false,
            IsGuest: false,
            Status: Status.Active
        );

        // Act
        var result = await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Grupo não encontrado.");
    }

    [Fact]
    public async Task CreateAsync_WhenUserNotExists_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenUserNotExists_ShouldReturnFailure));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo = new Mock<IRepositoryBase<PlayerEntity>>();
        var sut = new PlayerService(repo.Object, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        var req = new CreatePlayerDto(
            Name: "A",
            UserId: Guid.NewGuid(),
            GroupId: group.Id,
            SkillPoints: 0,
            IsGoalkeeper: false,
            IsGuest: false,
            Status: Status.Active
        );

        // Act
        var result = await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Usuário não encontrado.");
    }

    [Fact]
    public async Task CreateAsync_WhenPlayerAlreadyExistsInGroup_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenPlayerAlreadyExistsInGroup_ShouldReturnFailure));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        var user = new UserEntity("u", "f", "l", "mail@test.com", "hash", null, null);

        db.Groups.Add(group);
        db.Users.Add(user);

        var existing = new PlayerEntity("P", user.Id, group.Id, 0, false, false, Status.Active);
        db.Players.Add(existing);

        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo = new RepositoryBase<PlayerEntity>(db);
        var sut = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        var req = new CreatePlayerDto(
            Name: "A",
            UserId: user.Id,
            GroupId: group.Id,
            SkillPoints: 0,
            IsGoalkeeper: false,
            IsGuest: false,
            Status: Status.Active
        );

        // Act
        var result = await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Be("Player already exists in the group.");
    }

    [Fact]
    public async Task CreateAsync_WhenValid_ShouldAdd_AndSave_AndTrimName()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateAsync_WhenValid_ShouldAdd_AndSave_AndTrimName));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        var user = new UserEntity("u", "f", "l", "mail@test.com", "hash", null, null);

        db.Groups.Add(group);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo = new RepositoryBase<PlayerEntity>(db);
        var sut = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        var req = new CreatePlayerDto(
            Name: "  Caio  ",
            UserId: user.Id,
            GroupId: group.Id,
            SkillPoints: 10,
            IsGoalkeeper: true,
            IsGuest: false,
            Status: Status.Active
        );

        // Act
        var result = await sut.CreateAsync(req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Status.Should().Be(ResultStatus.Created);
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().NotBe(Guid.Empty);

        var created = await db.Players.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == result.Data.Id);
        created.Should().NotBeNull();
        created!.Name.Should().Be("Caio");
        created.UserId.Should().Be(user.Id);
        created.GroupId.Should().Be(group.Id);
        created.SkillPoints.Should().Be(10);
        created.IsGoalkeeper.Should().BeTrue();
        created.Status.Should().Be(Status.Active);
    }

    [Fact]
    public async Task UpdateAsync_WhenNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenNotFound_ShouldReturnFailure));

        var logger = new Mock<ILogger<PlayerService>>();
        var repo = new Mock<IRepositoryBase<PlayerEntity>>();

        repo.Setup(r => r.GetByIdIncludingInactiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEntity?)null);

        var sut = new PlayerService(repo.Object, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        var req = new UpdatePlayerDto(
            Name: "X",
            GroupId: Guid.NewGuid(),
            SkillPoints: 0,
            IsGoalkeeper: false,
            IsGuest: false,
            Status: Status.Active
        );

        // Act
        var result = await sut.UpdateAsync(Guid.NewGuid(), req, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Jogador não encontrado.");
    }

    [Fact]
    public async Task UpdateAsync_WhenValid_ShouldUpdateFields_AndSave()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenValid_ShouldUpdateFields_AndSave));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        var user = new UserEntity("u", "f", "l", "mail@test.com", "hash", null, null);

        db.Groups.Add(group);
        db.Users.Add(user);

        var existing = new PlayerEntity("Old", user.Id, group.Id, 1m, false, false, Status.Active);
        db.Players.Add(existing);

        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo = new RepositoryBase<PlayerEntity>(db);
        var sut = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        var req = new UpdatePlayerDto(
            Name: "  New Name  ",
            GroupId: group.Id,
            SkillPoints: 9.5m,
            IsGoalkeeper: true,
            IsGuest: false,
            Status: Status.Active
        );

        // Act
        var result = await sut.UpdateAsync(existing.Id, req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(existing.Id);

        var reloaded = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == existing.Id);
        reloaded.Name.Should().Be("New Name");           // trim
        reloaded.SkillPoints.Should().Be(9.5m);
        reloaded.IsGoalkeeper.Should().BeTrue();
        reloaded.Status.Should().Be(Status.Active);
        reloaded.GroupId.Should().Be(group.Id);
        reloaded.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task UpdateAsync_WhenNameInvalid_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenNameInvalid_ShouldThrow));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        var user = new UserEntity("u", "f", "l", "mail@test.com", "hash", null, null);

        db.Groups.Add(group);
        db.Users.Add(user);

        var existing = new PlayerEntity("Ok", user.Id, group.Id, 0m, false, false, Status.Active);
        db.Players.Add(existing);

        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo = new RepositoryBase<PlayerEntity>(db);
        var sut = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        var req = new UpdatePlayerDto(
            Name: "   ",
            GroupId: group.Id,
            SkillPoints: 0m,
            IsGoalkeeper: false,
            IsGuest: false,
            Status: Status.Active
        );

        // Act
        var act = async () => await sut.UpdateAsync(existing.Id, req, CancellationToken.None);

        // Assert
        // se a validação de nome estoura no PlayerEntity.Rename, geralmente é InvalidOperationException
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Player name is required.");
    }

    // ─── LeaveGroupAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveGroupAsync_ValidOwner_SetsIsGuestTrue_AndClearsUserId()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(LeaveGroupAsync_ValidOwner_SetsIsGuestTrue_AndClearsUserId));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        var user  = new UserEntity("u", "F", "L", "u@test.com", "hash", null, null);
        db.Groups.Add(group);
        db.Users.Add(user);

        var player = new PlayerEntity("P", user.Id, group.Id, 0m, false, false, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo   = new RepositoryBase<PlayerEntity>(db);
        var sut    = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        // Act
        var result = await sut.LeaveGroupAsync(player.Id, user.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var reloaded = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == player.Id);
        reloaded.IsGuest.Should().BeTrue("jogador que saiu deve virar convidado.");
        reloaded.UserId.Should().BeNull("vínculo com conta deve ser desfeito ao sair da patota.");
    }

    [Fact]
    public async Task LeaveGroupAsync_AfterLeave_ShouldNotAppearInGetByUserId()
    {
        // Arrange — garante que GetByUserIdAsync não retorna a patota após a saída
        await using var db = DbContextFactory.Create(nameof(LeaveGroupAsync_AfterLeave_ShouldNotAppearInGetByUserId));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        var user  = new UserEntity("u", "F", "L", "u@test.com", "hash", null, null);
        db.Groups.Add(group);
        db.Users.Add(user);

        var player = new PlayerEntity("P", user.Id, group.Id, 0m, false, false, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo   = new RepositoryBase<PlayerEntity>(db);
        var sut    = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        // Act
        await sut.LeaveGroupAsync(player.Id, user.Id, CancellationToken.None);
        var myPlayers = await sut.GetByUserIdAsync(user.Id, CancellationToken.None);

        // Assert — a patota não deve aparecer mais na lista do usuário
        myPlayers.Success.Should().BeTrue();
        myPlayers.Data.Should().NotContain(p => p.GroupId == group.Id,
            "após sair da patota o UserId é nulo e não deve aparecer em GetByUserIdAsync.");
    }

    [Fact]
    public async Task LeaveGroupAsync_PlayerNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(LeaveGroupAsync_PlayerNotFound_ShouldReturnFailure));

        var logger = new Mock<ILogger<PlayerService>>();
        var repo   = new Mock<IRepositoryBase<PlayerEntity>>();

        repo.Setup(r => r.GetByIdIncludingInactiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEntity?)null);

        var sut = new PlayerService(repo.Object, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        // Act
        var result = await sut.LeaveGroupAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Jogador não encontrado.");
    }

    [Fact]
    public async Task LeaveGroupAsync_WrongUser_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(LeaveGroupAsync_WrongUser_ShouldReturnFailure));

        var group   = new GroupEntity("G", null, Guid.NewGuid());
        var owner   = new UserEntity("owner", "O", "W", "o@test.com", "hash", null, null);
        var intruder = Guid.NewGuid();
        db.Groups.Add(group);
        db.Users.Add(owner);

        var player = new PlayerEntity("P", owner.Id, group.Id, 0m, false, false, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo   = new RepositoryBase<PlayerEntity>(db);
        var sut    = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        // Act — requestingUserId não é o dono do player
        var result = await sut.LeaveGroupAsync(player.Id, intruder, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Forbidden);
        result.Error.Should().Be("Sem permissão para esta operação.");
    }

    [Fact]
    public async Task UpdateAsync_WhenSkillPointsNegative_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenSkillPointsNegative_ShouldThrow));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        var user = new UserEntity("u", "f", "l", "mail@test.com", "hash", null, null);

        db.Groups.Add(group);
        db.Users.Add(user);

        var existing = new PlayerEntity("Ok", user.Id, group.Id, 0m, false, false, Status.Active);
        db.Players.Add(existing);

        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo = new RepositoryBase<PlayerEntity>(db);
        var sut = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        var req = new UpdatePlayerDto(
            Name: "Ok",
            GroupId: group.Id,
            SkillPoints: -1m,
            IsGoalkeeper: false,
            IsGuest: false,
            Status: Status.Active
        );

        // Act
        var act = async () => await sut.UpdateAsync(existing.Id, req, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("SkillPoints cannot be negative.");
    }

    // ─── RemoveFromGroupAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RemoveFromGroupAsync_WhenValid_ShouldSetIsGuestTrue_AndClearUserId()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(RemoveFromGroupAsync_WhenValid_ShouldSetIsGuestTrue_AndClearUserId));

        var group  = new GroupEntity("G", null, Guid.NewGuid());
        var user   = new UserEntity("u", "F", "L", "u@test.com", "hash", null, null);
        db.Groups.Add(group);
        db.Users.Add(user);

        var player = new PlayerEntity("Caio", user.Id, group.Id, 5m, false, false, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo   = new RepositoryBase<PlayerEntity>(db);
        var sut    = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        // Act
        var result = await sut.RemoveFromGroupAsync(player.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == player.Id);
        reloaded.IsGuest.Should().BeTrue("jogador removido deve virar convidado.");
        reloaded.UserId.Should().BeNull("vínculo com conta deve ser desfeito.");
    }

    [Fact]
    public async Task RemoveFromGroupAsync_WhenValid_ShouldPreserveOtherFields()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(RemoveFromGroupAsync_WhenValid_ShouldPreserveOtherFields));

        var group  = new GroupEntity("G", null, Guid.NewGuid());
        var user   = new UserEntity("u", "F", "L", "u@test.com", "hash", null, null);
        db.Groups.Add(group);
        db.Users.Add(user);

        var player = new PlayerEntity("Caio", user.Id, group.Id, 7.5m, true, false, Status.Active);
        player.SetAttackRating(8);
        player.SetDefenseRating(6);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo   = new RepositoryBase<PlayerEntity>(db);
        var sut    = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        // Act
        await sut.RemoveFromGroupAsync(player.Id, CancellationToken.None);

        // Assert — name, ratings e grupo devem ser preservados
        var reloaded = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == player.Id);
        reloaded.Name.Should().Be("Caio");
        reloaded.GroupId.Should().Be(group.Id);
        reloaded.SkillPoints.Should().Be(7.5m);
        reloaded.IsGoalkeeper.Should().BeTrue();
        reloaded.Status.Should().Be(Status.Active);
        reloaded.AttackRating.Should().Be(8);
        reloaded.DefenseRating.Should().Be(6);
    }

    [Fact]
    public async Task RemoveFromGroupAsync_WhenPlayerNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(RemoveFromGroupAsync_WhenPlayerNotFound_ShouldReturnFailure));

        var repo = new Mock<IRepositoryBase<PlayerEntity>>();
        repo.Setup(r => r.GetByIdIncludingInactiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEntity?)null);

        var logger = new Mock<ILogger<PlayerService>>();
        var sut    = new PlayerService(repo.Object, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        // Act
        var result = await sut.RemoveFromGroupAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Jogador não encontrado.");
    }

    [Fact]
    public async Task RemoveFromGroupAsync_WhenAlreadyGuestWithNoUser_ShouldReturnBadRequest()
    {
        // Arrange — convidado sem conta vinculada (caso já removido)
        await using var db = DbContextFactory.Create(nameof(RemoveFromGroupAsync_WhenAlreadyGuestWithNoUser_ShouldReturnBadRequest));

        var group  = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var guest = new PlayerEntity("Visitante", null, group.Id, 0m, false, true, Status.Active);
        db.Players.Add(guest);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo   = new RepositoryBase<PlayerEntity>(db);
        var sut    = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        // Act
        var result = await sut.RemoveFromGroupAsync(guest.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Be("Jogador já é convidado sem conta vinculada.");
    }

    [Fact]
    public async Task RemoveFromGroupAsync_WhenGuestWithLinkedAccount_ShouldSucceed()
    {
        // Arrange — convidado que ainda tem UserId (edge case: aceitar convite mas não mudar isGuest)
        await using var db = DbContextFactory.Create(nameof(RemoveFromGroupAsync_WhenGuestWithLinkedAccount_ShouldSucceed));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        var user  = new UserEntity("u", "F", "L", "u@test.com", "hash", null, null);
        db.Groups.Add(group);
        db.Users.Add(user);

        var player = new PlayerEntity("Fulano", user.Id, group.Id, 0m, false, false, Status.Active);
        player.SetIsGuest(true);  // guest mas ainda com UserId
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo   = new RepositoryBase<PlayerEntity>(db);
        var sut    = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        // Act
        var result = await sut.RemoveFromGroupAsync(player.Id, CancellationToken.None);

        // Assert — UserId deve ser limpo
        result.Success.Should().BeTrue();
        var reloaded = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == player.Id);
        reloaded.IsGuest.Should().BeTrue();
        reloaded.UserId.Should().BeNull();
    }

    [Fact]
    public async Task RemoveFromGroupAsync_WhenValid_ShouldSendPushNotification()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(RemoveFromGroupAsync_WhenValid_ShouldSendPushNotification));

        var group  = new GroupEntity("G", null, Guid.NewGuid());
        var user   = new UserEntity("u", "F", "L", "u@test.com", "hash", null, null);
        db.Groups.Add(group);
        db.Users.Add(user);

        var player = new PlayerEntity("Marlon", user.Id, group.Id, 0m, false, false, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var logger   = new Mock<ILogger<PlayerService>>();
        var repo     = new RepositoryBase<PlayerEntity>(db);
        var pushMock = new Mock<IPushService>();
        pushMock
            .Setup(p => p.SendToGroupAdminsAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PlayerService(repo, logger.Object, db, pushMock.Object, MatchServiceMock(), TestImageStorage.Create());

        // Act
        await sut.RemoveFromGroupAsync(player.Id, CancellationToken.None);

        // Assert — push enviado para admins do grupo com tipo correto
        pushMock.Verify(p => p.SendToGroupAdminsAsync(
            group.Id,
            It.Is<string>(t => t.Contains("removido")),
            It.Is<string>(b => b.Contains("Marlon")),
            It.Is<Dictionary<string, string>>(d => d["type"] == "player_removed"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveFromGroupAsync_WhenInactivePlayer_ShouldSucceed()
    {
        // Arrange — jogador inativo também pode ser removido
        await using var db = DbContextFactory.Create(nameof(RemoveFromGroupAsync_WhenInactivePlayer_ShouldSucceed));

        var group  = new GroupEntity("G", null, Guid.NewGuid());
        var user   = new UserEntity("u", "F", "L", "u@test.com", "hash", null, null);
        db.Groups.Add(group);
        db.Users.Add(user);

        var player = new PlayerEntity("Inativo", user.Id, group.Id, 0m, false, false, Status.Active);
        player.Inactivate();
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo   = new RepositoryBase<PlayerEntity>(db);
        var sut    = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        // Act
        var result = await sut.RemoveFromGroupAsync(player.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var reloaded = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == player.Id);
        reloaded.IsGuest.Should().BeTrue();
        reloaded.UserId.Should().BeNull();
    }

    // ─── ToggleGoalkeeperAsync ────────────────────────────────────────────────

    [Fact]
    public async Task ToggleGoalkeeperAsync_WhenLinePlayer_ShouldBecomeGoalkeeper()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ToggleGoalkeeperAsync_WhenLinePlayer_ShouldBecomeGoalkeeper));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        var user  = new UserEntity("u", "F", "L", "u@test.com", "hash", null, null);
        db.Groups.Add(group);
        db.Users.Add(user);

        var player = new PlayerEntity("Caio", user.Id, group.Id, 5m, false, false, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo   = new RepositoryBase<PlayerEntity>(db);
        var sut    = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        // Act
        var result = await sut.ToggleGoalkeeperAsync(player.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.IsGoalkeeper.Should().BeTrue();

        var reloaded = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == player.Id);
        reloaded.IsGoalkeeper.Should().BeTrue();
    }

    [Fact]
    public async Task ToggleGoalkeeperAsync_WhenGoalkeeper_ShouldBecomeLinePlayer()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ToggleGoalkeeperAsync_WhenGoalkeeper_ShouldBecomeLinePlayer));

        var group = new GroupEntity("G", null, Guid.NewGuid());
        var user  = new UserEntity("u", "F", "L", "u@test.com", "hash", null, null);
        db.Groups.Add(group);
        db.Users.Add(user);

        var player = new PlayerEntity("Felipe", user.Id, group.Id, 7m, true, false, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerService>>();
        var repo   = new RepositoryBase<PlayerEntity>(db);
        var sut    = new PlayerService(repo, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        // Act
        var result = await sut.ToggleGoalkeeperAsync(player.Id, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.IsGoalkeeper.Should().BeFalse();

        var reloaded = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == player.Id);
        reloaded.IsGoalkeeper.Should().BeFalse();
    }

    [Fact]
    public async Task ToggleGoalkeeperAsync_WhenPlayerNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ToggleGoalkeeperAsync_WhenPlayerNotFound_ShouldReturnFailure));

        var repo = new Mock<IRepositoryBase<PlayerEntity>>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEntity?)null);

        var logger = new Mock<ILogger<PlayerService>>();
        var sut    = new PlayerService(repo.Object, logger.Object, db, Mock.Of<IPushService>(), MatchServiceMock(), TestImageStorage.Create());

        // Act
        var result = await sut.ToggleGoalkeeperAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
        result.Error.Should().Be("Jogador não encontrado.");
    }
}
