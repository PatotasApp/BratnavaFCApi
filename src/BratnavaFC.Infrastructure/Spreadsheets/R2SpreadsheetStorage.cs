using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using BratnavaFC.Infrastructure.Cloudflare;
using Microsoft.Extensions.Options;

namespace BratnavaFC.Infrastructure.Spreadsheets;

public sealed class R2SpreadsheetStorage : ISpreadsheetStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucketName;

    public R2SpreadsheetStorage(IOptions<R2Options> r2Options, IOptions<SpreadsheetStorageOptions> options)
    {
        AWSConfigsS3.UseSignatureVersion4 = true;

        _bucketName = options.Value.BucketName;
        _client = new AmazonS3Client(r2Options.Value.AccessKey, r2Options.Value.SecretKey, new AmazonS3Config
        {
            ServiceURL = r2Options.Value.EndpointUrl,
            ForcePathStyle = true,
        });
    }

    public string Description => $"r2:{_bucketName}";

    public async Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct)
    {
        // R2 não aceita o payload com trailer: Content-Length conhecido (MemoryStream) e
        // assinatura padrão, como no R2ReplayUrlService.
        var buffer = content as MemoryStream;
        if (buffer is null)
        {
            buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
        }
        buffer.Position = 0;

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
            InputStream = buffer,
            ContentType = contentType,
            UseChunkEncoding = false,
            AutoCloseStream = false,
        }, ct);
    }

    public async Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            var response = await _client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = objectKey,
            }, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("Arquivo não encontrado no storage.", objectKey, ex);
        }
    }

    public Task DeleteAsync(string objectKey, CancellationToken ct)
        => _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
        }, ct);

    public void Dispose() => _client.Dispose();
}
