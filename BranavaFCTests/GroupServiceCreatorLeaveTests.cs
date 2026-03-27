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

namespace BranavaFC.Tests;

public class GroupServiceCreatorLeaveTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static GroupService CreateSut(BratnavaFC.Infrastructure.Data.AppDbContext db)
    {
        var logger = new Mock<ILogger<GroupService>>();
        var repo   = new RepositoryBase<GroupEntity>(db);
        var push   = Mock.Of<IPushService>();
        return new GroupService(db, logger.Object, repo, push);
    }

    // ─── Validações gerais ────────────────────────────────────────────────────

    [Fact]
    public async Task CreatorLeaveGroupAsync_GroupNotFound_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreatorLeaveGroupAsync_GroupNotFound_ShouldReturnFailure));
        var sut = CreateSut(db);

        var dto = new CreatorLeaveGroupDto(DeleteGroup: true);

        // Act
        var result = await sut.CreatorLeaveGroupAsync(Guid.NewGuid(), Guid.NewGuid(), dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task CreatorLeaveGroupAsync_NotCreator_ShouldReturnForbidden()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreatorLeaveGroupAsync_NotCreator_ShouldReturnForbidden));

        var creator  = new UserEntity("creator", "C", "R", "c@b.com", "hash", null, null);
        var nonOwner = new UserEntity("other",   "O", "T", "o@b.com", "hash", null, null);
        db.Users.AddRange(creator, nonOwner);

        var group = new GroupEntity("G", null, creator.Id);
        group.SetAdmins([creator.Id]);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var dto = new CreatorLeaveGroupDto(DeleteGroup: true);

        // Act — nonOwner tenta usar a operação de criador
        var result = await sut.CreatorLeaveGroupAsync(group.Id, nonOwner.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Forbidden);
    }

    [Fact]
    public async Task CreatorLeaveGroupAsync_NoOptionProvided_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreatorLeaveGroupAsync_NoOptionProvided_ShouldReturnFailure));

        var creator = new UserEntity("creator", "C", "R", "c@b.com", "hash", null, null);
        db.Users.Add(creator);

        var group = new GroupEntity("G", null, creator.Id);
        group.SetAdmins([creator.Id]);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Dto sem nenhuma opção válida
        var dto = new CreatorLeaveGroupDto(TransferToUserId: null, PromoteAndTransferUserId: null, DeleteGroup: false);

        // Act
        var result = await sut.CreatorLeaveGroupAsync(group.Id, creator.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    // ─── Opção DeleteGroup ────────────────────────────────────────────────────

    [Fact]
    public async Task CreatorLeaveGroupAsync_DeleteGroup_RemovesGroup()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreatorLeaveGroupAsync_DeleteGroup_RemovesGroup));

        var creator = new UserEntity("creator", "C", "R", "c@b.com", "hash", null, null);
        db.Users.Add(creator);

        var group = new GroupEntity("G", null, creator.Id);
        group.SetAdmins([creator.Id]);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var dto = new CreatorLeaveGroupDto(DeleteGroup: true);

        // Act
        var result = await sut.CreatorLeaveGroupAsync(group.Id, creator.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var exists = await db.Groups.IgnoreQueryFilters().AnyAsync(g => g.Id == group.Id);
        exists.Should().BeFalse();
    }

    // ─── Opção TransferToUserId ───────────────────────────────────────────────

    [Fact]
    public async Task CreatorLeaveGroupAsync_TransferToExistingAdmin_TransfersCreatorAndSetsGuest()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreatorLeaveGroupAsync_TransferToExistingAdmin_TransfersCreatorAndSetsGuest));

        var creator  = new UserEntity("creator", "C", "R", "c@b.com", "hash", null, null);
        var newOwner = new UserEntity("newowner", "N", "O", "n@b.com", "hash", null, null);
        db.Users.AddRange(creator, newOwner);

        var group = new GroupEntity("G", null, creator.Id);
        group.SetAdmins([creator.Id, newOwner.Id]);
        db.Groups.Add(group);

        // Player do criador neste grupo
        var creatorPlayer = new PlayerEntity("Criador", creator.Id, group.Id, 5m, false, false, Status.Active);
        db.Players.Add(creatorPlayer);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var dto = new CreatorLeaveGroupDto(TransferToUserId: newOwner.Id);

        // Act
        var result = await sut.CreatorLeaveGroupAsync(group.Id, creator.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var savedGroup = await db.Groups
            .IgnoreQueryFilters()
            .Include(g => g.Admins)
            .FirstAsync(g => g.Id == group.Id);

        savedGroup.CreatedByUserId.Should().Be(newOwner.Id);
        savedGroup.Admins.Should().NotContain(a => a.UserId == creator.Id);
        savedGroup.Admins.Should().Contain(a => a.UserId == newOwner.Id);

        var savedPlayer = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == creatorPlayer.Id);
        savedPlayer.IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task CreatorLeaveGroupAsync_TransferToNonAdmin_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreatorLeaveGroupAsync_TransferToNonAdmin_ShouldReturnFailure));

        var creator    = new UserEntity("creator", "C", "R", "c@b.com", "hash", null, null);
        var nonAdmin   = new UserEntity("nonadmin", "N", "A", "na@b.com", "hash", null, null);
        db.Users.AddRange(creator, nonAdmin);

        var group = new GroupEntity("G", null, creator.Id);
        group.SetAdmins([creator.Id]);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var dto = new CreatorLeaveGroupDto(TransferToUserId: nonAdmin.Id);

        // Act
        var result = await sut.CreatorLeaveGroupAsync(group.Id, creator.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    // ─── Opção PromoteAndTransferUserId ───────────────────────────────────────

    [Fact]
    public async Task CreatorLeaveGroupAsync_PromoteAndTransfer_AddsAdminTransfersCreatorAndSetsGuest()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreatorLeaveGroupAsync_PromoteAndTransfer_AddsAdminTransfersCreatorAndSetsGuest));

        var creator  = new UserEntity("creator",  "C", "R", "c@b.com", "hash", null, null);
        var promoted = new UserEntity("promoted", "P", "M", "p@b.com", "hash", null, null);
        db.Users.AddRange(creator, promoted);

        var group = new GroupEntity("G", null, creator.Id);
        group.SetAdmins([creator.Id]);
        db.Groups.Add(group);

        // Player do criador neste grupo
        var creatorPlayer = new PlayerEntity("Criador", creator.Id, group.Id, 3m, false, false, Status.Active);
        db.Players.Add(creatorPlayer);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var dto = new CreatorLeaveGroupDto(PromoteAndTransferUserId: promoted.Id);

        // Act
        var result = await sut.CreatorLeaveGroupAsync(group.Id, creator.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var savedGroup = await db.Groups
            .IgnoreQueryFilters()
            .Include(g => g.Admins)
            .FirstAsync(g => g.Id == group.Id);

        savedGroup.CreatedByUserId.Should().Be(promoted.Id);
        savedGroup.Admins.Should().Contain(a => a.UserId == promoted.Id);
        savedGroup.Admins.Should().NotContain(a => a.UserId == creator.Id);

        var savedPlayer = await db.Players.IgnoreQueryFilters().FirstAsync(p => p.Id == creatorPlayer.Id);
        savedPlayer.IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task CreatorLeaveGroupAsync_PromoteNonExistentUser_ShouldReturnFailure()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreatorLeaveGroupAsync_PromoteNonExistentUser_ShouldReturnFailure));

        var creator = new UserEntity("creator", "C", "R", "c@b.com", "hash", null, null);
        db.Users.Add(creator);

        var group = new GroupEntity("G", null, creator.Id);
        group.SetAdmins([creator.Id]);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var dto = new CreatorLeaveGroupDto(PromoteAndTransferUserId: Guid.NewGuid());

        // Act
        var result = await sut.CreatorLeaveGroupAsync(group.Id, creator.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }
}
