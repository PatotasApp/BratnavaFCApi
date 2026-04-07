namespace BratnavaFC.Application.Abstractions;

public interface IReplayUrlService
{
    string GeneratePresignedUrl(string objectKey);
    Task<(Stream Stream, string ContentType)> GetObjectStreamAsync(string objectKey, CancellationToken ct);
}
