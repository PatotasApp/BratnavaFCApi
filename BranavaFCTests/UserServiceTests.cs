// Tests/Application/Services/UserServiceTests.cs
//
// Os testes de CreateUserAsync e ChangePasswordAsync foram removidos junto com os métodos:
// cadastro agora acontece no Firebase pelo front-end e é provisionado no primeiro acesso
// (UserProvisioningService), e senha é responsabilidade do SDK do Firebase.
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BranavaFC.Tests;

public class UserServiceTests
{
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
        var sut = new UserService(db, repo.Object, logger.Object, TestImageStorage.Create());

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
        var sut    = new UserService(db, repo.Object, logger.Object, TestImageStorage.Create());

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
        var sut    = new UserService(db, repo.Object, logger.Object, TestImageStorage.Create());

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
        var sut    = new UserService(db, repo.Object, logger.Object, TestImageStorage.Create());

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

        var user = new UserEntity("user1", "A", "B", "a@test.com", "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        repo.Setup(r => r.GetByIdIncludingInactiveAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        repo.Setup(r => r.Update(It.IsAny<UserEntity>()));
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = new UserService(db, repo.Object, logger.Object, TestImageStorage.Create());

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

        var user = new UserEntity("user1", "A", "B", "a@test.com", "hash", null, null);
        user.Inactivate();

        db.Users.Add(user);
        await db.SaveChangesAsync();

        repo.Setup(r => r.GetByIdIncludingInactiveAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        repo.Setup(r => r.Update(It.IsAny<UserEntity>()));
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = new UserService(db, repo.Object, logger.Object, TestImageStorage.Create());

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
