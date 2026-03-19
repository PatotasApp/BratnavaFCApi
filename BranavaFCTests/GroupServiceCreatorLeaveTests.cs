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

public class GroupServiceCreatorLeaveTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static GroupService CreateSut(BratnavaFC.Infrastructure.Data.AppDbContext db)
    {
        var logger = new Mock<ILogger<GroupService>>();
        var repo   = new RepositoryBase<GroupEntity>(db);
        return new GroupService(db, logger.Object, repo);
    }

    // ─── Validações gerais ────────────────────────────────────────────────────

    [Fact]
    public async Task CreatorLeaveGroupAsync_GroupNotFound_ThrowsApplicationException()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreatorLeaveGroupAsync_GroupNotFound_ThrowsApplicationException));
        var sut = CreateSut(db);

        var dto = new CreatorLeaveGroupDto(DeleteGroup: true);

        // Act
        var act = async () => await sut.CreatorLeaveGroupAsync(Guid.NewGuid(), Guid.NewGuid(), dto, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("Group not found.");
    }

    [Fact]
    public async Task CreatorLeaveGroupAsync_NotCreator_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreatorLeaveGroupAsync_NotCreator_ThrowsUnauthorizedAccessException));

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
        var act = async () => await sut.CreatorLeaveGroupAsync(group.Id, nonOwner.Id, dto, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Only the group creator can use this operation.");
    }

    [Fact]
    public async Task CreatorLeaveGroupAsync_NoOptionProvided_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreatorLeaveGroupAsync_NoOptionProvided_ThrowsInvalidOperationException));

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
        var act = async () => await sut.CreatorLeaveGroupAsync(group.Id, creator.Id, dto, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Invalid leave operation: provide TransferToUserId, PromoteAndTransferUserId, or set DeleteGroup = true.");
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
        await sut.CreatorLeaveGroupAsync(group.Id, creator.Id, dto, CancellationToken.None);

        // Assert
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
        await sut.CreatorLeaveGroupAsync(group.Id, creator.Id, dto, CancellationToken.None);

        // Assert
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
    public async Task CreatorLeaveGroupAsync_TransferToNonAdmin_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreatorLeaveGroupAsync_TransferToNonAdmin_ThrowsInvalidOperationException));

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
        var act = async () => await sut.CreatorLeaveGroupAsync(group.Id, creator.Id, dto, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("TransferToUserId must be an existing admin.");
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
        await sut.CreatorLeaveGroupAsync(group.Id, creator.Id, dto, CancellationToken.None);

        // Assert
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
    public async Task CreatorLeaveGroupAsync_PromoteNonExistentUser_ThrowsApplicationException()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreatorLeaveGroupAsync_PromoteNonExistentUser_ThrowsApplicationException));

        var creator = new UserEntity("creator", "C", "R", "c@b.com", "hash", null, null);
        db.Users.Add(creator);

        var group = new GroupEntity("G", null, creator.Id);
        group.SetAdmins([creator.Id]);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var dto = new CreatorLeaveGroupDto(PromoteAndTransferUserId: Guid.NewGuid());

        // Act
        var act = async () => await sut.CreatorLeaveGroupAsync(group.Id, creator.Id, dto, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("User to promote not found.");
    }
}
