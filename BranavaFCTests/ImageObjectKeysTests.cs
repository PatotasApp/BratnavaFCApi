namespace BranavaFC.Tests;

public sealed class ImageObjectKeysTests
{
    private static readonly Guid OwnerId = Guid.Parse("3f2a8c11-0000-0000-0000-000000000001");

    [Fact]
    public void New_ForAvatar_UsesAvatarsPrefixAndJpg()
    {
        var key = ImageObjectKeys.New(ImageKind.Avatar, OwnerId);

        Assert.StartsWith($"avatars/{OwnerId}/", key);
        Assert.EndsWith(".jpg", key);
    }

    /// <summary>
    /// Logo sai em PNG, não JPEG: o alfa de um logo com fundo transparente não sobrevive a
    /// uma recodificação em JPEG.
    /// </summary>
    [Fact]
    public void New_ForLogo_UsesLogosPrefixAndPng()
    {
        var key = ImageObjectKeys.New(ImageKind.Logo, OwnerId);

        Assert.StartsWith($"logos/{OwnerId}/", key);
        Assert.EndsWith(".png", key);
    }

    /// <summary>
    /// Avatar e logo dividem bucket. Se os prefixos colidissem, a logo de um grupo poderia
    /// ocupar a key do avatar de um usuário com o mesmo id.
    /// </summary>
    [Fact]
    public void New_KeepsKindsInSeparateFolders()
    {
        Assert.NotEqual(
            ImageObjectKeys.New(ImageKind.Avatar, OwnerId).Split('/')[0],
            ImageObjectKeys.New(ImageKind.Logo, OwnerId).Split('/')[0]);
    }

    /// <summary>
    /// A key é o cache-buster: duas imagens do mesmo dono nunca podem colidir, senão o
    /// cliente serve a imagem antiga sob a URL da nova.
    /// </summary>
    [Fact]
    public void New_GeneratesDistinctKeyPerCall()
    {
        Assert.NotEqual(
            ImageObjectKeys.New(ImageKind.Avatar, OwnerId),
            ImageObjectKeys.New(ImageKind.Avatar, OwnerId));
    }

    [Theory]
    [InlineData("https://pub-abc.r2.dev", "avatars/x/y.jpg")]
    [InlineData("https://pub-abc.r2.dev/", "avatars/x/y.jpg")]
    [InlineData("https://pub-abc.r2.dev", "/avatars/x/y.jpg")]
    [InlineData("https://pub-abc.r2.dev/", "/avatars/x/y.jpg")]
    public void BuildPublicUrl_JoinsWithExactlyOneSlash(string baseUrl, string objectKey)
    {
        var url = ImageObjectKeys.BuildPublicUrl(baseUrl, objectKey);

        Assert.Equal("https://pub-abc.r2.dev/avatars/x/y.jpg", url);
    }
}
