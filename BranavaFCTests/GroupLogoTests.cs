using BratnavaFC.Domain.Entities;

namespace BranavaFC.Tests;

public sealed class GroupLogoTests
{
    [Fact]
    public void SetLogo_StoresImageMetadata()
    {
        var group = new GroupEntity("Patota", null, Guid.NewGuid());
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        group.SetLogo(bytes, "image/png");

        Assert.Same(bytes, group.LogoData);
        Assert.Equal("image/png", group.LogoContentType);
        Assert.NotNull(group.LogoUpdatedAt);
    }

    [Fact]
    public void RemoveLogo_ClearsStoredImage()
    {
        var group = new GroupEntity("Patota", null, Guid.NewGuid());
        group.SetLogo(new byte[] { 1 }, "image/png");

        group.RemoveLogo();

        Assert.Null(group.LogoData);
        Assert.Null(group.LogoContentType);
        Assert.Null(group.LogoUpdatedAt);
    }
}
