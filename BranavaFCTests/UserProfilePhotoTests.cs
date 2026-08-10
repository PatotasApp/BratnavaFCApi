using BratnavaFC.Domain.Entities;

namespace BranavaFC.Tests;

public sealed class UserProfilePhotoTests
{
    [Fact]
    public void SetProfilePhoto_StoresImageMetadata()
    {
        var user = CreateUser();
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0x00 };

        user.SetProfilePhoto(bytes, "image/jpeg");

        Assert.Same(bytes, user.ProfilePhotoData);
        Assert.Equal("image/jpeg", user.ProfilePhotoContentType);
        Assert.NotNull(user.ProfilePhotoUpdatedAt);
    }

    [Fact]
    public void RemoveProfilePhoto_ClearsStoredImage()
    {
        var user = CreateUser();
        user.SetProfilePhoto(new byte[] { 1 }, "image/jpeg");

        user.RemoveProfilePhoto();

        Assert.Null(user.ProfilePhotoData);
        Assert.Null(user.ProfilePhotoContentType);
        Assert.Null(user.ProfilePhotoUpdatedAt);
    }

    [Fact]
    public void SetProfilePhoto_RejectsEmptyImage()
    {
        var user = CreateUser();
        Assert.Throws<InvalidOperationException>(() => user.SetProfilePhoto([], "image/jpeg"));
    }

    private static UserEntity CreateUser() => new(
        "user",
        "Usuário",
        "Teste",
        "user@example.com",
        "hashed-password",
        null,
        null);
}
