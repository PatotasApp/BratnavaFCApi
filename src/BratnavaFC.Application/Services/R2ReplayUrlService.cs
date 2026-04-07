using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using BratnavaFC.Application.Abstractions;

namespace BratnavaFC.Application.Services;

public sealed class R2ReplayUrlService : IReplayUrlService, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucketName;

    public R2ReplayUrlService()
    {
        var endpointUrl = Environment.GetEnvironmentVariable("CLOUDFLARE_R2_ENDPOINT_URL")
            ?? throw new InvalidOperationException("CLOUDFLARE_R2_ENDPOINT_URL não configurado.");

        var accessKeyId = Environment.GetEnvironmentVariable("CLOUDFLARE_R2_ACCESS_KEY_ID")
            ?? throw new InvalidOperationException("CLOUDFLARE_R2_ACCESS_KEY_ID não configurado.");

        var secretKey = Environment.GetEnvironmentVariable("CLOUDFLARE_R2_SECRET_ACCESS_KEY")
            ?? throw new InvalidOperationException("CLOUDFLARE_R2_SECRET_ACCESS_KEY não configurado.");

        _bucketName = Environment.GetEnvironmentVariable("CLOUDFLARE_R2_BUCKET_NAME") ?? "goal-replays";

        AWSConfigsS3.UseSignatureVersion4 = true;

        _client = new AmazonS3Client(accessKeyId, secretKey, new AmazonS3Config
        {
            ServiceURL     = endpointUrl,
            ForcePathStyle = true,
        });
    }

    public string GeneratePresignedUrl(string objectKey) =>
        _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
            Expires = DateTime.UtcNow.AddHours(1),
            Verb = HttpVerb.GET,
        });

    public async Task<(Stream Stream, string ContentType)> GetObjectStreamAsync(string objectKey, CancellationToken ct)
    {
        var response = await _client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
        }, ct);

        return (response.ResponseStream, response.Headers.ContentType ?? "video/mp4");
    }

    public void Dispose() => _client.Dispose();
}
