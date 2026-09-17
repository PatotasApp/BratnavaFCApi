using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Dublê do storage de avatares para os testes que só precisam que o serviço exista.
/// BuildPublicUrl devolve uma URL determinística a partir da key, para que um teste possa
/// afirmar sobre a URL sem depender de configuração real de bucket.
/// </summary>
internal static class TestImageStorage
{
    public const string BaseUrl = "https://cdn.test";

    public static IImageStorageService Create()
    {
        var mock = new Mock<IImageStorageService>();
        mock.SetupGet(x => x.IsEnabled).Returns(true);
        mock.SetupGet(x => x.PublicBaseUrl).Returns(BaseUrl);
        mock.Setup(x => x.BuildPublicUrl(It.IsAny<string>()))
            .Returns((string key) => $"{BaseUrl}/{key}");
        return mock.Object;
    }
}
