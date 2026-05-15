namespace BratnavaFC.Application.Abstractions;

public interface IReplayUrlService
{
    string GeneratePresignedUrl(string objectKey);
    Task<(Stream Stream, string ContentType)> GetObjectStreamAsync(string objectKey, CancellationToken ct);
    Task DeleteObjectAsync(string objectKey, CancellationToken ct);
    /// <summary>Faz upload de um stream para o bucket R2 e retorna o ETag.</summary>
    Task<string> UploadObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct);
    string BucketName { get; }
}
