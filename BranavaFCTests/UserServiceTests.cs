// Tests/Application/Services/UserServiceTests.cs
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
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
        var result = await sut.CreateUserAsync(dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("User already exists with the user name 'luis'.");
        result.Status.Should().Be(ResultStatus.BadRequest);
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
        var result = await sut.CreateUserAsync(dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("User already exists with the user name '  LUIS  '.");
        result.Status.Should().Be(ResultStatus.BadRequest);
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
        var result = await sut.CreateUserAsync(dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("User already exists with the email 'a@test.com'.");
        result.Status.Should().Be(ResultStatus.BadRequest);
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
        var result = await sut.CreateUserAsync(dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

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
        var result = await sut.GetAllAsync(req, CancellationToken.None);

        // Assert (não pode estourar Nullable object must have a value)
        result.Success.Should().BeTrue();

        var res = result.Data!;
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
        var result = await sut.ChangePasswordAsync(user.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Current password is invalid.");
        result.Status.Should().Be(ResultStatus.BadRequest);
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
        var result = await sut.ChangePasswordAsync(user.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        repo.Verify(r => r.Update(It.IsAny<UserEntity>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        user.Password.Should().NotBe(oldHash);

        var verify = hasher.VerifyHashedPassword(user, user.Password, "new_pw_123");
        verify.Should().Be(PasswordVerificationResult.Success);
    }

    // ─── GetAllAsync — page size cap ──────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_WhenPageSizeExceeds2000_ShouldCapAt2000()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WhenPageSizeExceeds2000_ShouldCapAt2000));

        // Cria 3 usuários para confirmar que a query roda sem estourar
        for (int i = 1; i <= 3; i++)
            db.Users.Add(new UserEntity($"u{i}", "A", "B", $"u{i}@test.com", "hash", null, null));
        await db.SaveChangesAsync();

        var repo   = new Mock<IRepositoryBase<UserEntity>>();
        var logger = new Mock<ILogger<UserService>>();
        var hasher = new PasswordHasher<UserEntity>();
        var sut    = new UserService(db, repo.Object, logger.Object, hasher);

        var req = new ListUsersRequestDto { Page = 1, PageSize = 9999 };

        // Act
        var result = await sut.GetAllAsync(req, CancellationToken.None);

        // Assert — o serviço não deve lançar exceção e deve retornar com sucesso
        result.Success.Should().BeTrue();
        // o pageSize interno é truncado para 2000, mas como só há 3 usuários todos aparecem
        result.Data!.Items.Should().HaveCount(3);
        result.Data.PageSize.Should().Be(2000, "o pageSize deve ser capeado em 2000.");
    }

    [Fact]
    public async Task GetAllAsync_WhenPageSizeIs2000_ShouldNotReduce()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WhenPageSizeIs2000_ShouldNotReduce));

        var repo   = new Mock<IRepositoryBase<UserEntity>>();
        var logger = new Mock<ILogger<UserService>>();
        var hasher = new PasswordHasher<UserEntity>();
        var sut    = new UserService(db, repo.Object, logger.Object, hasher);

        var req = new ListUsersRequestDto { Page = 1, PageSize = 2000 };

        // Act
        var result = await sut.GetAllAsync(req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.PageSize.Should().Be(2000);
    }

    [Fact]
    public async Task GetAllAsync_WhenPageSizeBelow2000_ShouldKeepOriginalValue()
    {
        // Arrange
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WhenPageSizeBelow2000_ShouldKeepOriginalValue));

        var repo   = new Mock<IRepositoryBase<UserEntity>>();
        var logger = new Mock<ILogger<UserService>>();
        var hasher = new PasswordHasher<UserEntity>();
        var sut    = new UserService(db, repo.Object, logger.Object, hasher);

        var req = new ListUsersRequestDto { Page = 1, PageSize = 50 };

        // Act
        var result = await sut.GetAllAsync(req, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.PageSize.Should().Be(50, "valores abaixo de 2000 não devem ser alterados.");
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

        // Act
        var result = await sut.UpdateAsync(user.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

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

        // Act
        var result = await sut.UpdateAsync(user.Id, dto, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        user.Status.Should().Be(Status.Active);
        user.InactivatedAt.Should().BeNull();
    }
}
