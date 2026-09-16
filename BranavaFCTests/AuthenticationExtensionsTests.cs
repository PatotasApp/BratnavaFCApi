using BratnavaFC.Api.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BranavaFC.Tests;

public class AuthenticationExtensionsTests
{
    [Fact]
    public void AddJwtAuthentication_UsesConfiguredProjectId_WithoutFirebaseAdmin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Firebase:ProjectId"] = "development-d04ef"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddJwtAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(
            "https://securetoken.google.com/development-d04ef",
            options.Authority);
        Assert.Equal(
            "development-d04ef",
            options.TokenValidationParameters.ValidAudience);
    }
}
