using BratnavaFC.Domain.Entities;

namespace BranavaFC.Tests;

public sealed class UserProfilePhotoTests
{
    [Fact]
    public void SetProfilePhoto_StoresObjectKey()
    {
        var user = CreateUser();

        user.SetProfilePhoto("development/3f2a/8c1d.jpg");

        Assert.Equal("development/3f2a/8c1d.jpg", user.ProfilePhotoKey);
        Assert.NotNull(user.ProfilePhotoUpdatedAt);
    }

    [Fact]
    public void RemoveProfilePhoto_ClearsStoredKey()
    {
        var user = CreateUser();
        user.SetProfilePhoto("development/3f2a/8c1d.jpg");

        user.RemoveProfilePhoto();

        Assert.Null(user.ProfilePhotoKey);
        Assert.Null(user.ProfilePhotoUpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetProfilePhoto_RejectsBlankObjectKey(string objectKey)
    {
        var user = CreateUser();

        Assert.Throws<InvalidOperationException>(() => user.SetProfilePhoto(objectKey));
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
