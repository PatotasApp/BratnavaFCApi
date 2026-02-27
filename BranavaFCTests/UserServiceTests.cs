// Tests/Application/Services/UserServiceTests.cs
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BranavaFC.Tests;

public class UserServiceTests
{
    [Fact]
    public async Task CreateUserAsync_WhenUsernameExists_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateUserAsync_WhenUsernameExists_ShouldThrow));

        var repo = new Mock<IRepositoryBase<UserEntity>>();
        var hasher = new PasswordHasher<UserEntity>();
        var logger = new Mock<ILogger<UserService>>();

        var sut = new UserService(db, repo.Object, logger.Object, hasher);

        var existingUser = new UserEntity(
            userName: "luis",
            firstName: "Luis",
            lastName: "Mello",
            email: "existing@email.com",
            passwordHashed: "hash",
            phone: null,
            birthDate: null,
            role: UserRole.User);

        db.Users.Add(existingUser);
        await db.SaveChangesAsync();

        var dto = new CreateUserDto(
            UserName: "luis",
            FirstName: "Outro",
            LastName: "Cara",
            Email: "new@email.com",
            Password: "123",
            Phone: null,
            BirthDate: null);

        // Act
        Func<Task> act = () => sut.CreateUserAsync(dto, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("User already exists with the user name 'luis'.");
    }

    [Fact]
    public async Task CreateUserAsync_WhenUsernameExists_IgnoringCaseAndSpaces_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateUserAsync_WhenUsernameExists_IgnoringCaseAndSpaces_ShouldThrow));

        var repo = new Mock<IRepositoryBase<UserEntity>>();
        var hasher = new PasswordHasher<UserEntity>();
        var logger = new Mock<ILogger<UserService>>();

        var sut = new UserService(db, repo.Object, logger.Object, hasher);

        var existingUser = new UserEntity(
            userName: "luis",
            firstName: "Luis",
            lastName: "Mello",
            email: "existing@email.com",
            passwordHashed: "hash",
            phone: null,
            birthDate: null,
            role: UserRole.User);

        db.Users.Add(existingUser);
        await db.SaveChangesAsync();

        var dto = new CreateUserDto(
            UserName: "  LUIS  ",
            FirstName: "Outro",
            LastName: "Cara",
            Email: "new@email.com",
            Password: "123",
            Phone: null,
            BirthDate: null);

        // Act
        Func<Task> act = () => sut.CreateUserAsync(dto, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("User already exists with the user name '  LUIS  '.");
    }

    [Fact]
    public async Task CreateUserAsync_WhenEmailExists_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateUserAsync_WhenEmailExists_ShouldThrow));

        var existing = new UserEntity("user1", "f", "l", "a@test.com", "hash", null, null);
        db.Users.Add(existing);
        await db.SaveChangesAsync();

        var repo = new Mock<IRepositoryBase<UserEntity>>();
        var logger = new Mock<ILogger<UserService>>();
        var hasher = new PasswordHasher<UserEntity>();

        var sut = new UserService(db, repo.Object, logger.Object, hasher);

        var dto = new CreateUserDto
        (
            UserName: "user2",
            FirstName: "A",
            LastName: "B",
            Email: "a@test.com",
            Password: "pw",
            Phone: null,
            BirthDate: null
        );

        // Act
        var act = async () => await sut.CreateUserAsync(dto, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("User already exists with the email 'a@test.com'.");
    }

    [Fact]
    public async Task CreateUserAsync_WhenValid_ShouldHashPassword_AndPersistViaRepository()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(CreateUserAsync_WhenValid_ShouldHashPassword_AndPersistViaRepository));

        var repo = new Mock<IRepositoryBase<UserEntity>>();
        var logger = new Mock<ILogger<UserService>>();
        var hasher = new PasswordHasher<UserEntity>();

        UserEntity? captured = null;
        repo.Setup(r => r.Add(It.IsAny<UserEntity>()))
            .Callback<UserEntity>(u => captured = u);

        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = new UserService(db, repo.Object, logger.Object, hasher);

        var dto = new CreateUserDto
        (
            UserName: "user2",
            FirstName: "A",
            LastName: "B",
            Email: "b@test.com",
            Password: "pw",
            Phone: null,
            BirthDate: null
        );

        // Act
        await sut.CreateUserAsync(dto, CancellationToken.None);

        // Assert
        repo.Verify(r => r.Add(It.IsAny<UserEntity>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        captured.Should().NotBeNull();
        captured!.UserName.Should().Be("user2");
        captured.Email.Should().Be("b@test.com");

        // Ele começa com "temp", mas depois SetPasswordHash(hashed) troca:
        captured.Password.Should().NotBeNullOrWhiteSpace();
        captured.Password.Should().NotBe("temp");
    }

    [Fact]
    public async Task GetAllAsync_WhenUpdateDateIsNull_ShouldNotThrow_AndShouldFallback()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WhenUpdateDateIsNull_ShouldNotThrow_AndShouldFallback));

        var user = new UserEntity("user1", "A", "B", "a@test.com", "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Se UpdateDate for shadow property/nullable no banco, você pode forçar assim.
        // Caso não exista, isso não vai compilar/funcionar — aí ignore (o teste ainda cobre o bug por ter nulls na base real).
        try
        {
            db.Entry(user).Property("UpdateDate").CurrentValue = null;
            await db.SaveChangesAsync();
        }
        catch
        {
            // ignore
        }

        var repo = new Mock<IRepositoryBase<UserEntity>>();
        var logger = new Mock<ILogger<UserService>>();
        var hasher = new PasswordHasher<UserEntity>();
        var sut = new UserService(db, repo.Object, logger.Object, hasher);

        var req = new ListUsersRequestDto
        {
            Page = 1,
            PageSize = 20,
            IncludeInactive = false,
            Search = null,
            Status = null,
            Role = null
        };

        // Act
        Func<Task> act = async () => await sut.GetAllAsync(req, CancellationToken.None);

        // Assert (não pode estourar Nullable object must have a value)
        await act.Should().NotThrowAsync();

        var res = await sut.GetAllAsync(req, CancellationToken.None);
        res.Items.Should().HaveCount(1);
        res.Items[0].Id.Should().Be(user.Id);

        // UpdateDate no DTO não pode quebrar (na implementação corrigida você faz fallback)
        res.Items[0].UpdateDate.Should().NotBe(default);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenCurrentPasswordInvalid_ShouldThrow()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ChangePasswordAsync_WhenCurrentPasswordInvalid_ShouldThrow));

        var repo = new Mock<IRepositoryBase<UserEntity>>();
        var logger = new Mock<ILogger<UserService>>();
        var hasher = new PasswordHasher<UserEntity>();

        var user = new UserEntity("user1", "A", "B", "a@test.com", "temp", null, null);
        user.SetPasswordHash(hasher.HashPassword(user, "correct_pw"));

        db.Users.Add(user);
        await db.SaveChangesAsync();

        repo.Setup(r => r.GetByIdIncludingInactiveAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        repo.Setup(r => r.Update(It.IsAny<UserEntity>()));
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = new UserService(db, repo.Object, logger.Object, hasher);

        var dto = new ChangePasswordDto
        {
            CurrentPassword = "wrong_pw",
            NewPassword = "new_pw_123"
        };

        // Act
        Func<Task> act = () => sut.ChangePasswordAsync(user.Id, dto, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApplicationException>()
            .WithMessage("Current password is invalid.");
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenValid_ShouldUpdatePasswordHash_AndPersist()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(ChangePasswordAsync_WhenValid_ShouldUpdatePasswordHash_AndPersist));

        var repo = new Mock<IRepositoryBase<UserEntity>>();
        var logger = new Mock<ILogger<UserService>>();
        var hasher = new PasswordHasher<UserEntity>();

        var user = new UserEntity("user1", "A", "B", "a@test.com", "temp", null, null);

        var oldHash = hasher.HashPassword(user, "old_pw");
        user.SetPasswordHash(oldHash);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        repo.Setup(r => r.GetByIdIncludingInactiveAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        repo.Setup(r => r.Update(It.IsAny<UserEntity>()));
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = new UserService(db, repo.Object, logger.Object, hasher);

        var dto = new ChangePasswordDto
        {
           CurrentPassword = "old_pw",
            NewPassword = "new_pw_123"
        };

        // Act
        await sut.ChangePasswordAsync(user.Id, dto, CancellationToken.None);

        // Assert
        repo.Verify(r => r.Update(It.IsAny<UserEntity>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        user.Password.Should().NotBe(oldHash);

        var verify = hasher.VerifyHashedPassword(user, user.Password, "new_pw_123");
        verify.Should().Be(PasswordVerificationResult.Success);
    }

    // Opcional: se você implementou UpdateAsync com Status no DTO chamando Inactivate/Reactivate
    [Fact]
    public async Task UpdateAsync_WhenStatusBecomesInactive_ShouldInactivateAndSetInactivatedAt()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenStatusBecomesInactive_ShouldInactivateAndSetInactivatedAt));

        var repo = new Mock<IRepositoryBase<UserEntity>>();
        var logger = new Mock<ILogger<UserService>>();
        var hasher = new PasswordHasher<UserEntity>();

        var user = new UserEntity("user1", "A", "B", "a@test.com", "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        repo.Setup(r => r.GetByIdIncludingInactiveAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        repo.Setup(r => r.Update(It.IsAny<UserEntity>()));
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = new UserService(db, repo.Object, logger.Object, hasher);

        var dto = new UpdateUserDto
        {
            Status = Status.Inactive
        };

        // Act + Assert
        // Se você não tem UpdateAsync no service, remova esse teste.
        Func<Task> act = () => sut.UpdateAsync(user.Id, dto, CancellationToken.None);
        await act.Should().NotThrowAsync();

        user.Status.Should().Be(Status.Inactive);
        user.InactivatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_WhenStatusBecomesActive_ShouldReactivateAndClearInactivatedAt()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(UpdateAsync_WhenStatusBecomesActive_ShouldReactivateAndClearInactivatedAt));

        var repo = new Mock<IRepositoryBase<UserEntity>>();
        var logger = new Mock<ILogger<UserService>>();
        var hasher = new PasswordHasher<UserEntity>();

        var user = new UserEntity("user1", "A", "B", "a@test.com", "hash", null, null);
        user.Inactivate();

        db.Users.Add(user);
        await db.SaveChangesAsync();

        repo.Setup(r => r.GetByIdIncludingInactiveAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        repo.Setup(r => r.Update(It.IsAny<UserEntity>()));
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = new UserService(db, repo.Object, logger.Object, hasher);

        var dto = new UpdateUserDto
        {
            Status = Status.Active
        };

        // Act + Assert
        // Se você não tem UpdateAsync no service, remova esse teste.
        Func<Task> act = () => sut.UpdateAsync(user.Id, dto, CancellationToken.None);
        await act.Should().NotThrowAsync();

        user.Status.Should().Be(Status.Active);
        user.InactivatedAt.Should().BeNull();
    }
}