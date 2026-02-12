using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;

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
}
