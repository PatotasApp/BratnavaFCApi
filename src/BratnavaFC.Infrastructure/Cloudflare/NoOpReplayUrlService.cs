using Microsoft.Extensions.Logging;

namespace BratnavaFC.Infrastructure.Cloudflare;

/// <summary>
/// Registrado quando o R2 está desabilitado no ambiente. Escritas fingem sucesso para o
/// fluxo do chamador seguir; leituras falham explicitamente, porque devolver bytes vazios
/// como se fossem vídeo esconderia o problema em vez de sinalizá-lo.
/// </summary>
public sealed class NoOpReplayUrlService : IReplayUrlService
{
    private const string DependencyName = "Cloudflare R2";

    private readonly ILogger<NoOpReplayUrlService> _logger;
    private readonly string _environmentName;

    public NoOpReplayUrlService(
        ILogger<NoOpReplayUrlService> logger,
        string environmentName)
    {
        _logger = logger;
        _environmentName = environmentName;
    }

    public bool IsEnabled => false;

    public string BucketName => "goal-replays";

    /// <summary>
    /// Nunca lança: é chamado dentro de projeções de lista de clipes, onde uma exceção
    /// derrubaria o endpoint inteiro em vez de degradar um item.
    /// </summary>
    public string GeneratePresignedUrl(string objectKey)
        => $"https://storage-disabled.local/{BucketName}/{objectKey}";

    public Task<(Stream Stream, string ContentType)> GetObjectStreamAsync(string objectKey, CancellationToken ct)
        => throw new DependencyDisabledException(DependencyName, _environmentName);

    public Task DeleteObjectAsync(string objectKey, CancellationToken ct)
    {
        _logger.LogInformation(
            "[R2] Delete ignorado porque o storage esta desabilitado no ambiente {Environment}. Key={ObjectKey}",
            _environmentName,
            objectKey);

        return Task.CompletedTask;
    }

    public async Task<string> UploadObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct)
    {
        // Drena o stream para o chamador não ficar com um corpo de request pendurado.
        await content.CopyToAsync(Stream.Null, ct);

        _logger.LogInformation(
            "[R2] Upload ignorado porque o storage esta desabilitado no ambiente {Environment}. Key={ObjectKey}",
            _environmentName,
            objectKey);

        return "\"storage-disabled\"";
    }
}
