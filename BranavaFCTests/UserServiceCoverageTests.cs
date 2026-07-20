using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

public class UserServiceCoverageTests
{
    private static UserService Sut(AppDbContext db, IPushService? push = null)
        => new(
            db,
            new RepositoryBase<UserEntity>(db),
            Mock.Of<ILogger<UserService>>(),
            new PasswordHasher<UserEntity>(),
            push ?? Mock.Of<IPushService>());

    private static UserEntity User(string userName, string first, string last, string email,
        UserRole role = UserRole.User)
        => new(userName, first, last, email, "hash", null, null, role);

    // ─── GetUserByIdAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserByIdAsync_WhenFound_ReturnsDtoWithPlayerAndAdminIds()
    {
        await using var db = DbContextFactory.Create(nameof(GetUserByIdAsync_WhenFound_ReturnsDtoWithPlayerAndAdminIds));
        var user = User("luis", "Luis", "Mello", "l@test.com");
        db.Users.Add(user);
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);
        var player = new PlayerEntity("Luis", user.Id, group.Id, 0m, false, false, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.GetUserByIdAsync(user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.UserName.Should().Be("luis");
        result.Data.Email.Should().Be("l@test.com");
        result.Data.PlayerIds.Should().Contain(player.Id);
    }

    [Fact]
    public async Task GetUserByIdAsync_WhenNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(GetUserByIdAsync_WhenNotFound_ReturnsNotFound));
        var sut = Sut(db);

        var result = await sut.GetUserByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // ─── GetAllAsync filters ─────────────────────────────────────────────────

    private static async Task SeedListUsers(AppDbContext db)
    {
        var active1 = User("alice", "Alice", "Alpha", "alice@test.com");
        var active2 = User("bob", "Bob", "Bravo", "bob@test.com", UserRole.Admin);
        var inactive = User("carol", "Carol", "Charlie", "carol@test.com");
        inactive.Inactivate();
        db.Users.AddRange(active1, active2, inactive);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAllAsync_ByDefault_ExcludesInactiveUsers()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_ByDefault_ExcludesInactiveUsers));
        await SeedListUsers(db);
        var sut = Sut(db);

        var result = await sut.GetAllAsync(new ListUsersRequestDto { IncludeInactive = false }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);
        result.Data.Items.Should().NotContain(u => u.UserName == "carol");
    }

    [Fact]
    public async Task GetAllAsync_WithIncludeInactive_ReturnsAllUsers()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WithIncludeInactive_ReturnsAllUsers));
        await SeedListUsers(db);
        var sut = Sut(db);

        var result = await sut.GetAllAsync(new ListUsersRequestDto { IncludeInactive = true }, CancellationToken.None);

        result.Data!.Items.Should().HaveCount(3);
        result.Data.Total.Should().Be(3);
    }

    [Fact]
    public async Task GetAllAsync_WithSearch_MatchesNameUserNameAndEmail()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WithSearch_MatchesNameUserNameAndEmail));
        await SeedListUsers(db);
        var sut = Sut(db);

        var byUserName = await sut.GetAllAsync(new ListUsersRequestDto { Search = "ALICE" }, CancellationToken.None);
        byUserName.Data!.Items.Should().ContainSingle().Which.UserName.Should().Be("alice");

        var byLastName = await sut.GetAllAsync(new ListUsersRequestDto { Search = "bravo" }, CancellationToken.None);
        byLastName.Data!.Items.Should().ContainSingle().Which.UserName.Should().Be("bob");

        var byEmail = await sut.GetAllAsync(new ListUsersRequestDto { Search = "bob@test" }, CancellationToken.None);
        byEmail.Data!.Items.Should().ContainSingle().Which.UserName.Should().Be("bob");

        var noMatch = await sut.GetAllAsync(new ListUsersRequestDto { Search = "zzz" }, CancellationToken.None);
        noMatch.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_WithStatusFilter_ReturnsOnlyMatching()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WithStatusFilter_ReturnsOnlyMatching));
        await SeedListUsers(db);
        var sut = Sut(db);

        var result = await sut.GetAllAsync(
            new ListUsersRequestDto { IncludeInactive = true, Status = Status.Inactive }, CancellationToken.None);

        result.Data!.Items.Should().ContainSingle().Which.UserName.Should().Be("carol");
    }

    [Fact]
    public async Task GetAllAsync_WithRoleFilter_ReturnsOnlyMatching()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WithRoleFilter_ReturnsOnlyMatching));
        await SeedListUsers(db);
        var sut = Sut(db);

        var result = await sut.GetAllAsync(
            new ListUsersRequestDto { Role = (int)UserRole.Admin }, CancellationToken.None);

        result.Data!.Items.Should().ContainSingle().Which.UserName.Should().Be("bob");
    }

    [Fact]
    public async Task GetAllAsync_WithNonPositivePageAndPageSize_UsesDefaults()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WithNonPositivePageAndPageSize_UsesDefaults));
        await SeedListUsers(db);
        var sut = Sut(db);

        var result = await sut.GetAllAsync(new ListUsersRequestDto { Page = 0, PageSize = -5 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Page.Should().Be(1);
        result.Data.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task GetAllAsync_Paginates_OrderedByFirstName()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_Paginates_OrderedByFirstName));
        await SeedListUsers(db);
        var sut = Sut(db);

        var page1 = await sut.GetAllAsync(new ListUsersRequestDto { Page = 1, PageSize = 1 }, CancellationToken.None);
        var page2 = await sut.GetAllAsync(new ListUsersRequestDto { Page = 2, PageSize = 1 }, CancellationToken.None);

        page1.Data!.Items.Should().ContainSingle().Which.FirstName.Should().Be("Alice");
        page2.Data!.Items.Should().ContainSingle().Which.FirstName.Should().Be("Bob");
        page1.Data.Total.Should().Be(2);
    }

    // ─── UpdateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WhenUserNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenUserNotFound_ReturnsNotFound));
        var sut = Sut(db);

        var result = await sut.UpdateAsync(Guid.NewGuid(), new UpdateUserDto(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task UpdateAsync_WhenUserNameTaken_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenUserNameTaken_ReturnsBadRequest));
        var target = User("target", "T", "T", "t@test.com");
        var other = User("taken", "O", "O", "o@test.com");
        db.Users.AddRange(target, other);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.UpdateAsync(target.Id, new UpdateUserDto { UserName = "  TAKEN " }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Contain("user name");
    }

    [Fact]
    public async Task UpdateAsync_WhenEmailTaken_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenEmailTaken_ReturnsBadRequest));
        var target = User("target", "T", "T", "t@test.com");
        var other = User("other", "O", "O", "taken@test.com");
        db.Users.AddRange(target, other);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.UpdateAsync(target.Id, new UpdateUserDto { Email = "TAKEN@test.com" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Contain("email");
    }

    [Fact]
    public async Task UpdateAsync_WithNewUserNameAndEmail_UpdatesBoth()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WithNewUserNameAndEmail_UpdatesBoth));
        var user = User("old", "F", "L", "old@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.UpdateAsync(user.Id,
            new UpdateUserDto { UserName = "newname", Email = "new@test.com" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == user.Id);
        reloaded.UserName.Should().Be("newname");
        reloaded.Email.Should().Be("new@test.com");
    }

    [Fact]
    public async Task UpdateAsync_WithProfileFields_UpdatesProfile()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WithProfileFields_UpdatesProfile));
        var user = User("u", "OldFirst", "OldLast", "u@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var birth = new DateTimeOffset(1990, 5, 20, 0, 0, 0, TimeSpan.Zero);

        var result = await sut.UpdateAsync(user.Id, new UpdateUserDto
        {
            FirstName = "NewFirst",
            BirthDate = birth,
            Phone = "11999999999"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == user.Id);
        reloaded.FirstName.Should().Be("NewFirst");
        reloaded.LastName.Should().Be("OldLast", "last name not sent should be preserved");
        reloaded.BirthDate.Should().Be(birth);
        reloaded.Phone.Should().Be("11999999999");
    }

    [Fact]
    public async Task UpdateAsync_WithInvalidRole_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WithInvalidRole_ReturnsBadRequest));
        var user = User("u", "F", "L", "u@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.UpdateAsync(user.Id, new UpdateUserDto { Role = 999 }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Invalid role.");
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task UpdateAsync_WithValidRole_UpdatesRole()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WithValidRole_UpdatesRole));
        var user = User("u", "F", "L", "u@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.UpdateAsync(user.Id,
            new UpdateUserDto { Role = (int)UserRole.Admin }, CancellationToken.None);

        result.Success.Should().BeTrue();
        (await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == user.Id))
            .Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task UpdateAsync_WithInvalidStatus_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WithInvalidStatus_ReturnsBadRequest));
        var user = User("u", "F", "L", "u@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.UpdateAsync(user.Id, new UpdateUserDto { Status = (Status)999 }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Invalid status.");
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    // ─── ChangePasswordAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ChangePasswordAsync_WhenCurrentPasswordMissing_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(ChangePasswordAsync_WhenCurrentPasswordMissing_ReturnsBadRequest));
        var sut = Sut(db);

        var result = await sut.ChangePasswordAsync(Guid.NewGuid(),
            new ChangePasswordDto { CurrentPassword = "  ", NewPassword = "new" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("CurrentPassword is required.");
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenNewPasswordMissing_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(ChangePasswordAsync_WhenNewPasswordMissing_ReturnsBadRequest));
        var sut = Sut(db);

        var result = await sut.ChangePasswordAsync(Guid.NewGuid(),
            new ChangePasswordDto { CurrentPassword = "old", NewPassword = "" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("NewPassword is required.");
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenUserNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(ChangePasswordAsync_WhenUserNotFound_ReturnsNotFound));
        var sut = Sut(db);

        var result = await sut.ChangePasswordAsync(Guid.NewGuid(),
            new ChangePasswordDto { CurrentPassword = "old", NewPassword = "new" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenValid_NotifiesUser()
    {
        await using var db = DbContextFactory.Create(nameof(ChangePasswordAsync_WhenValid_NotifiesUser));
        var hasher = new PasswordHasher<UserEntity>();
        var user = User("u", "F", "L", "u@test.com");
        user.SetPasswordHash(hasher.HashPassword(user, "old_pw"));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var push = new Mock<IPushService>();
        push.Setup(p => p.SendToUserAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Returns(Task.CompletedTask);

        var sut = Sut(db, push.Object);

        var result = await sut.ChangePasswordAsync(user.Id,
            new ChangePasswordDto { CurrentPassword = "old_pw", NewPassword = "new_pw" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == user.Id);
        hasher.VerifyHashedPassword(reloaded, reloaded.Password, "new_pw")
            .Should().Be(PasswordVerificationResult.Success);
    }

    // ─── Inactivate / Reactivate ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteAccountAsync_WhenValid_RemovesUserAndKeepsPlayerAsGuest()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAccountAsync_WhenValid_RemovesUserAndKeepsPlayerAsGuest));
        var hasher = new PasswordHasher<UserEntity>();
        var user = User("delete_me", "Delete", "Me", "delete@test.com");
        user.SetPasswordHash(hasher.HashPassword(user, "old_pw"));
        var group = new GroupEntity("G", null, Guid.NewGuid());
        var player = new PlayerEntity("Delete Me", user.Id, group.Id, 3m, false);

        db.Users.Add(user);
        db.Groups.Add(group);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.DeleteAccountAsync(user.Id, new DeleteAccountDto
        {
            Password = "old_pw",
            Confirmation = "EXCLUIR"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        (await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Id == user.Id)).Should().BeFalse();

        var reloadedPlayer = await db.Players.IgnoreQueryFilters().SingleAsync(x => x.Id == player.Id);
        reloadedPlayer.UserId.Should().BeNull();
        reloadedPlayer.IsGuest.Should().BeTrue();
        reloadedPlayer.Name.Should().Be("Delete Me");
    }

    [Fact]
    public async Task DeleteAccountAsync_WhenUserOwnsActiveGroup_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(DeleteAccountAsync_WhenUserOwnsActiveGroup_ReturnsBadRequest));
        var hasher = new PasswordHasher<UserEntity>();
        var user = User("owner", "Group", "Owner", "owner@test.com");
        user.SetPasswordHash(hasher.HashPassword(user, "old_pw"));
        var group = new GroupEntity("Patota", null, user.Id);

        db.Users.Add(user);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.DeleteAccountAsync(user.Id, new DeleteAccountDto
        {
            Password = "old_pw",
            Confirmation = "EXCLUIR"
        }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        (await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Id == user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task InactivateAsync_WhenNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(InactivateAsync_WhenNotFound_ReturnsNotFound));
        var sut = Sut(db);

        var result = await sut.InactivateAsync(Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task InactivateAsync_WhenFound_InactivatesUser()
    {
        await using var db = DbContextFactory.Create(nameof(InactivateAsync_WhenFound_InactivatesUser));
        var user = User("u", "F", "L", "u@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.InactivateAsync(user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == user.Id);
        reloaded.Status.Should().Be(Status.Inactive);
        reloaded.InactivatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReactivateAsync_WhenNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(ReactivateAsync_WhenNotFound_ReturnsNotFound));
        var sut = Sut(db);

        var result = await sut.ReactivateAsync(Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task ReactivateAsync_WhenFound_ReactivatesUser()
    {
        await using var db = DbContextFactory.Create(nameof(ReactivateAsync_WhenFound_ReactivatesUser));
        var user = User("u", "F", "L", "u@test.com");
        user.Inactivate();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        var result = await sut.ReactivateAsync(user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        var reloaded = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == user.Id);
        reloaded.Status.Should().Be(Status.Active);
        reloaded.InactivatedAt.Should().BeNull();
    }
}
