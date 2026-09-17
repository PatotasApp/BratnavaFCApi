using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Troca de avatar: o que importa aqui é a ordem das operações. A key nova só vale depois do
/// commit, e a antiga só pode morrer depois disso.
/// </summary>
public sealed class UserServiceSetPhotoTests
{
    [Fact]
    public async Task SetPhotoAsync_PersistsTheKeyReturnedByStorage()
    {
        var (sut, user, _, storage) = await BuildAsync(nameof(SetPhotoAsync_PersistsTheKeyReturnedByStorage));
        storage.Setup(x => x.UploadAsync(ImageKind.Avatar, user.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("development/u/nova.jpg");

        var result = await sut.SetPhotoAsync(user.Id, new MemoryStream([1]), CancellationToken.None);

        result.Success.Should().BeTrue();
        user.ProfilePhotoKey.Should().Be("development/u/nova.jpg");
        result.Data!.PhotoUrl.Should().Be($"{TestImageStorage.BaseUrl}/development/u/nova.jpg");
    }

    [Fact]
    public async Task SetPhotoAsync_DeletesThePreviousObject()
    {
        var (sut, user, _, storage) = await BuildAsync(nameof(SetPhotoAsync_DeletesThePreviousObject));
        user.SetProfilePhoto("development/u/antiga.jpg");
        storage.Setup(x => x.UploadAsync(ImageKind.Avatar, user.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("development/u/nova.jpg");

        await sut.SetPhotoAsync(user.Id, new MemoryStream([1]), CancellationToken.None);

        storage.Verify(x => x.DeleteAsync("development/u/antiga.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetPhotoAsync_WithoutPreviousPhoto_DeletesNothing()
    {
        var (sut, user, _, storage) = await BuildAsync(nameof(SetPhotoAsync_WithoutPreviousPhoto_DeletesNothing));
        storage.Setup(x => x.UploadAsync(ImageKind.Avatar, user.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("development/u/nova.jpg");

        await sut.SetPhotoAsync(user.Id, new MemoryStream([1]), CancellationToken.None);

        storage.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Um objeto órfão no bucket custa alguns KB; derrubar a troca de foto que o usuário
    /// acabou de fazer custa a troca inteira. O delete é best-effort de propósito.
    /// </summary>
    [Fact]
    public async Task SetPhotoAsync_SucceedsEvenWhenDeletingThePreviousObjectFails()
    {
        var (sut, user, _, storage) = await BuildAsync(nameof(SetPhotoAsync_SucceedsEvenWhenDeletingThePreviousObjectFails));
        user.SetProfilePhoto("development/u/antiga.jpg");
        storage.Setup(x => x.UploadAsync(ImageKind.Avatar, user.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("development/u/nova.jpg");
        storage.Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("R2 fora do ar"));

        var result = await sut.SetPhotoAsync(user.Id, new MemoryStream([1]), CancellationToken.None);

        result.Success.Should().BeTrue();
        user.ProfilePhotoKey.Should().Be("development/u/nova.jpg");
    }

    /// <summary>
    /// Se o upload falha, nada muda: a foto anterior continua sendo a que o usuário vê.
    /// </summary>
    [Fact]
    public async Task SetPhotoAsync_WhenUploadFails_KeepsThePreviousKey()
    {
        var (sut, user, _, storage) = await BuildAsync(nameof(SetPhotoAsync_WhenUploadFails_KeepsThePreviousKey));
        user.SetProfilePhoto("development/u/antiga.jpg");
        storage.Setup(x => x.UploadAsync(ImageKind.Avatar, user.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("R2 fora do ar"));

        var result = await sut.SetPhotoAsync(user.Id, new MemoryStream([1]), CancellationToken.None);

        result.Success.Should().BeFalse();
        user.ProfilePhotoKey.Should().Be("development/u/antiga.jpg");
        storage.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetPhotoAsync_WhenImageIsInvalid_FailsAsBadRequest()
    {
        var (sut, user, _, storage) = await BuildAsync(nameof(SetPhotoAsync_WhenImageIsInvalid_FailsAsBadRequest));
        storage.Setup(x => x.UploadAsync(ImageKind.Avatar, user.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidImageException());

        var result = await sut.SetPhotoAsync(user.Id, new MemoryStream([1]), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task RemovePhotoAsync_ClearsTheKeyAndDeletesTheObject()
    {
        var (sut, user, _, storage) = await BuildAsync(nameof(RemovePhotoAsync_ClearsTheKeyAndDeletesTheObject));
        user.SetProfilePhoto("development/u/antiga.jpg");

        var result = await sut.RemovePhotoAsync(user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        user.ProfilePhotoKey.Should().BeNull();
        storage.Verify(x => x.DeleteAsync("development/u/antiga.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<(UserService Sut, UserEntity User, Mock<IRepositoryBase<UserEntity>> Repo, Mock<IImageStorageService> Storage)>
        BuildAsync(string dbName)
    {
        var db = DbContextFactory.Create(dbName);
        var user = new UserEntity("user", "A", "B", "a@test.com", "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var repo = new Mock<IRepositoryBase<UserEntity>>();
        repo.Setup(x => x.GetByIdIncludingInactiveAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var storage = new Mock<IImageStorageService>();
        storage.Setup(x => x.BuildPublicUrl(It.IsAny<string>()))
               .Returns((string key) => $"{TestImageStorage.BaseUrl}/{key}");

        var sut = new UserService(db, repo.Object, Mock.Of<ILogger<UserService>>(), storage.Object);
        return (sut, user, repo, storage);
    }
}
