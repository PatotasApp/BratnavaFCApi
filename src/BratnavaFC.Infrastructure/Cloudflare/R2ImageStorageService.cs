using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace BratnavaFC.Infrastructure.Cloudflare;

public sealed class R2ImageStorageService : IImageStorageService, IDisposable
{
    /// <summary>
    /// Um ano, immutable. Só é seguro porque a key carrega um GUID: a URL de uma imagem nunca
    /// passa a apontar para outro conteúdo, então o cliente e a borda podem parar de
    /// revalidar. Trocar a foto produz key nova, não conteúdo novo na mesma key.
    /// </summary>
    private const string CacheControl = "public, max-age=31536000, immutable";

    private readonly AmazonS3Client _client;
    private readonly IImageProcessor _processor;
    private readonly ImageStorageOptions _options;

    /// <summary>
    /// Host público já normalizado. O serviço é singleton, então a normalização acontece uma
    /// vez no construtor e o getter vira leitura de campo — não há custo por acesso, e ele é
    /// lido dentro de projeções que rodam por request.
    ///
    /// O TrimEnd existe porque o valor vem de um secret digitado à mão: com barra no fim a
    /// URL viraria ".../r2.dev//avatars/x.jpg", e no R2 caminho duplo é OUTRA key — daria 404
    /// em toda imagem, sem erro visível em lugar nenhum.
    /// </summary>
    private readonly string _publicBaseUrl;

    public R2ImageStorageService(
        IOptions<R2Options> r2Options,
        IOptions<ImageStorageOptions> imageOptions,
        IImageProcessor processor)
    {
        AWSConfigsS3.UseSignatureVersion4 = true;

        _options = imageOptions.Value;
        _publicBaseUrl = _options.PublicBaseUrl.TrimEnd('/');
        _processor = processor;
        _client = new AmazonS3Client(r2Options.Value.AccessKey, r2Options.Value.SecretKey, new AmazonS3Config
        {
            ServiceURL = r2Options.Value.EndpointUrl,
            ForcePathStyle = true,
        });
    }

    public bool IsEnabled => true;

    public string PublicBaseUrl => _publicBaseUrl;

    public string BuildPublicUrl(string objectKey)
        => ImageObjectKeys.BuildPublicUrl(_publicBaseUrl, objectKey);

    public async Task<string> UploadAsync(ImageKind kind, Guid ownerId, Stream image, CancellationToken ct)
    {
        using var normalized = await _processor.NormalizeAsync(kind, image, ct);
        var objectKey = ImageObjectKeys.New(kind, ownerId);

        // Mesmo motivo do R2ReplayUrlService: o R2 não aceita o payload com trailer, então o
        // MemoryStream garante Content-Length conhecido e assinatura padrão.
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            InputStream = normalized,
            ContentType = ImageKinds.ContentTypeOf(kind),
            UseChunkEncoding = false,
            Headers = { CacheControl = CacheControl },
        }, ct);

        return objectKey;
    }

    public Task DeleteAsync(string objectKey, CancellationToken ct)
        => _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
        }, ct);

    public void Dispose() => _client.Dispose();
}
