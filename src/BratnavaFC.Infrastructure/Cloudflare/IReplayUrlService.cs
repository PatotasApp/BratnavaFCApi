namespace BratnavaFC.Infrastructure.Cloudflare;

public interface IReplayUrlService
{
    /// <summary>
    /// Falso quando o storage está desabilitado no ambiente. O proxy de streaming precisa
    /// consultar isso antes de tentar baixar da URL, porque ele escreve direto no Response
    /// e não passa pelo exception handler global.
    /// </summary>
    bool IsEnabled { get; }

    string GeneratePresignedUrl(string objectKey);
    Task<(Stream Stream, string ContentType)> GetObjectStreamAsync(string objectKey, CancellationToken ct);
    Task DeleteObjectAsync(string objectKey, CancellationToken ct);
    /// <summary>Faz upload de um stream para o bucket R2 e retorna o ETag.</summary>
    Task<string> UploadObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct);
    string BucketName { get; }
}
