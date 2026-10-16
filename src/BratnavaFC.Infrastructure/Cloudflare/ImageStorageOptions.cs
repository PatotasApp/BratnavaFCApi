namespace BratnavaFC.Infrastructure.Cloudflare;

public sealed class ImageStorageOptions
{
    public const string SectionName = "Cloudflare:R2:Images";

    public bool Enabled { get; set; } = true;

    /// <summary>Um bucket por ambiente: bratnava-development / bratnava-production.</summary>
    public string BucketName { get; set; } = "";

    /// <summary>Host público do bucket — r2.dev hoje, domínio custom quando o DNS migrar.</summary>
    public string PublicBaseUrl { get; set; } = "";
}
