using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Troca de logo do grupo. Mesma ordem de operações do avatar: sobe, commita, e só então
/// apaga a anterior.
/// </summary>
public sealed class GroupServiceSetLogoTests
{
    [Fact]
    public async Task SetLogoAsync_PersistsTheKeyReturnedByStorage()
    {
        var (sut, db, group, storage) = await BuildAsync(nameof(SetLogoAsync_PersistsTheKeyReturnedByStorage));
        storage.Setup(x => x.UploadAsync(ImageKind.Logo, group.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("logos/g/nova.png");

        var result = await sut.SetLogoAsync(group.Id, new MemoryStream([1]), CancellationToken.None);

        result.Success.Should().BeTrue();
        (await db.Groups.FirstAsync()).LogoKey.Should().Be("logos/g/nova.png");
        result.Data!.LogoUrl.Should().Be($"{TestImageStorage.BaseUrl}/logos/g/nova.png");
    }

    /// <summary>
    /// Avatar e logo dividem o mesmo bucket, então o tipo precisa chegar certo ao storage —
    /// é ele que decide prefixo, formato de saída e se a imagem é recortada.
    /// </summary>
    [Fact]
    public async Task SetLogoAsync_UploadsAsLogoKind()
    {
        var (sut, _, group, storage) = await BuildAsync(nameof(SetLogoAsync_UploadsAsLogoKind));
        storage.Setup(x => x.UploadAsync(It.IsAny<ImageKind>(), It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("logos/g/nova.png");

        await sut.SetLogoAsync(group.Id, new MemoryStream([1]), CancellationToken.None);

        storage.Verify(
            x => x.UploadAsync(ImageKind.Logo, group.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetLogoAsync_DeletesThePreviousObject()
    {
        var (sut, _, group, storage) = await BuildAsync(nameof(SetLogoAsync_DeletesThePreviousObject), "logos/g/antiga.png");
        storage.Setup(x => x.UploadAsync(ImageKind.Logo, group.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("logos/g/nova.png");

        await sut.SetLogoAsync(group.Id, new MemoryStream([1]), CancellationToken.None);

        storage.Verify(x => x.DeleteAsync("logos/g/antiga.png", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetLogoAsync_SucceedsEvenWhenDeletingThePreviousObjectFails()
    {
        var (sut, db, group, storage) = await BuildAsync(nameof(SetLogoAsync_SucceedsEvenWhenDeletingThePreviousObjectFails), "logos/g/antiga.png");
        storage.Setup(x => x.UploadAsync(ImageKind.Logo, group.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("logos/g/nova.png");
        storage.Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("R2 fora do ar"));

        var result = await sut.SetLogoAsync(group.Id, new MemoryStream([1]), CancellationToken.None);

        result.Success.Should().BeTrue();
        (await db.Groups.FirstAsync()).LogoKey.Should().Be("logos/g/nova.png");
    }

    [Fact]
    public async Task SetLogoAsync_WhenUploadFails_KeepsThePreviousKey()
    {
        var (sut, db, group, storage) = await BuildAsync(nameof(SetLogoAsync_WhenUploadFails_KeepsThePreviousKey), "logos/g/antiga.png");
        storage.Setup(x => x.UploadAsync(ImageKind.Logo, group.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("R2 fora do ar"));

        var result = await sut.SetLogoAsync(group.Id, new MemoryStream([1]), CancellationToken.None);

        result.Success.Should().BeFalse();
        (await db.Groups.FirstAsync()).LogoKey.Should().Be("logos/g/antiga.png");
        storage.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetLogoAsync_WhenImageIsInvalid_FailsAsBadRequest()
    {
        var (sut, _, group, storage) = await BuildAsync(nameof(SetLogoAsync_WhenImageIsInvalid_FailsAsBadRequest));
        storage.Setup(x => x.UploadAsync(ImageKind.Logo, group.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidImageException());

        var result = await sut.SetLogoAsync(group.Id, new MemoryStream([1]), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task RemoveLogoAsync_ClearsTheKeyAndDeletesTheObject()
    {
        var (sut, db, group, storage) = await BuildAsync(nameof(RemoveLogoAsync_ClearsTheKeyAndDeletesTheObject), "logos/g/antiga.png");

        var result = await sut.RemoveLogoAsync(group.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        (await db.Groups.FirstAsync()).LogoKey.Should().BeNull();
        storage.Verify(x => x.DeleteAsync("logos/g/antiga.png", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<(GroupService Sut, AppDbContext Db, GroupEntity Group, Mock<IImageStorageService> Storage)>
        BuildAsync(string dbName, string? existingLogoKey = null)
    {
        var db = DbContextFactory.Create(dbName);
        var group = new GroupEntity("Patota", null, Guid.NewGuid());
        if (existingLogoKey is not null) group.SetLogo(existingLogoKey);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var storage = new Mock<IImageStorageService>();
        storage.Setup(x => x.BuildPublicUrl(It.IsAny<string>()))
               .Returns((string key) => $"{TestImageStorage.BaseUrl}/{key}");

        var sut = new GroupService(
            db,
            Mock.Of<ILogger<GroupService>>(),
            new RepositoryBase<GroupEntity>(db),
            Mock.Of<IPushService>(),
            storage.Object);

        return (sut, db, group, storage);
    }
}
