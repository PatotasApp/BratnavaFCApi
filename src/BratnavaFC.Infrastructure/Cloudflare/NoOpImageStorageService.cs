using Microsoft.Extensions.Logging;

namespace BratnavaFC.Infrastructure.Cloudflare;

/// <summary>
/// Registrado quando o R2 está desabilitado no ambiente (Development local). Segue a mesma
/// política do <see cref="NoOpReplayUrlService"/>: escrita finge sucesso para o fluxo do
/// chamador seguir, e a URL devolvida aponta para um host inexistente — os clients já caem
/// nas iniciais quando a imagem falha, então degrada em vez de quebrar a tela.
/// </summary>
public sealed class NoOpImageStorageService : IImageStorageService
{
    private const string DisabledHost = "https://storage-disabled.local";

    private readonly ILogger<NoOpImageStorageService> _logger;
    private readonly string _environmentName;

    public NoOpImageStorageService(ILogger<NoOpImageStorageService> logger, string environmentName)
    {
        _logger = logger;
        _environmentName = environmentName;
    }

    public bool IsEnabled => false;

    public string PublicBaseUrl => DisabledHost;

    public string BuildPublicUrl(string objectKey) => $"{DisabledHost}/{objectKey}";

    public async Task<string> UploadAsync(ImageKind kind, Guid ownerId, Stream image, CancellationToken ct)
    {
        // Drena o stream pelo mesmo motivo do NoOpReplayUrlService: sem isso o corpo do
        // request fica pendurado esperando ser lido.
        await image.CopyToAsync(Stream.Null, ct);

        var objectKey = ImageObjectKeys.New(kind, ownerId);

        _logger.LogInformation(
            "[R2] Upload de imagem ignorado porque o storage esta desabilitado no ambiente {Environment}. Key={ObjectKey}",
            _environmentName,
            objectKey);

        return objectKey;
    }

    public Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        _logger.LogInformation(
            "[R2] Delete de imagem ignorado porque o storage esta desabilitado no ambiente {Environment}. Key={ObjectKey}",
            _environmentName,
            objectKey);

        return Task.CompletedTask;
    }
}
