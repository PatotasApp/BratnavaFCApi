using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BratnavaFC.Infrastructure.Cloudflare.Extensions
{
    public static class CloudflareExtensions
    {
        public static void AddCloudflareR2Bucket(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<R2Options>().Bind(configuration.GetSection(R2Options.SectionName))
                    .Validate(o =>
                        !string.IsNullOrWhiteSpace(o.AccessKey) &&
                        !string.IsNullOrWhiteSpace(o.SecretKey) &&
                        !string.IsNullOrWhiteSpace(o.EndpointUrl),
                        "Configuração do R2 incompleta.")
                    .ValidateOnStart();
        }
    }
}
