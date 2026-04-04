namespace BratnavaFC.Application.Abstractions;

public interface IReplayUrlService
{
    string GeneratePresignedUrl(string objectKey);
}
