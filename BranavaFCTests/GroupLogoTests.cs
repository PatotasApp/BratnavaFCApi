using BratnavaFC.Domain.Entities;

namespace BranavaFC.Tests;

public sealed class GroupLogoTests
{
    [Fact]
    public void SetLogo_StoresObjectKey()
    {
        var group = CreateGroup();

        group.SetLogo("logos/3f2a/8c1d.png");

        Assert.Equal("logos/3f2a/8c1d.png", group.LogoKey);
        Assert.NotNull(group.LogoUpdatedAt);
    }

    [Fact]
    public void RemoveLogo_ClearsStoredKey()
    {
        var group = CreateGroup();
        group.SetLogo("logos/3f2a/8c1d.png");

        group.RemoveLogo();

        Assert.Null(group.LogoKey);
        Assert.Null(group.LogoUpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetLogo_RejectsBlankObjectKey(string objectKey)
    {
        var group = CreateGroup();

        Assert.Throws<InvalidOperationException>(() => group.SetLogo(objectKey));
    }

    private static GroupEntity CreateGroup() => new("Patota", null, Guid.NewGuid());
}
