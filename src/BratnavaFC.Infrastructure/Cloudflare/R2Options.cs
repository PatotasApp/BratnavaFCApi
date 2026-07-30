namespace BratnavaFC.Infrastructure.Cloudflare
{
    public sealed class R2Options
    {
        public const string SectionName = "Cloudflare:R2";
        public string AccessKey { get; set; } = "";
        public string SecretKey { get; set; } = "";
        public string EndpointUrl { get; set; } = "";
    }
}
