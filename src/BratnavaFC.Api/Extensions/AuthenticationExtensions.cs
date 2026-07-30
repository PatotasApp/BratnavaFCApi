using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace BratnavaFC.Api.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSecret = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("Jwt:SecretKey nao configurado.");

        var jwtIssuer = configuration["Jwt:Issuer"] ?? "TeamManagement";
        var jwtAudience = configuration["Jwt:Audience"] ?? "account";

        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudience = jwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtSecret)),
                    RoleClaimType = "role",
                    NameClaimType = "name"
                };

                options.Events = new JwtBearerEvents
                {
                    // Elementos <video src> e o cliente SignalR não mandam header
                    // Authorization; ambos passam o token na query string.
                    OnMessageReceived = context =>
                    {
                        var path = context.Request.Path.Value ?? string.Empty;

                        if (path.Contains("/stream", StringComparison.OrdinalIgnoreCase))
                        {
                            var token = context.Request.Query["t"].FirstOrDefault();

                            if (!string.IsNullOrEmpty(token))
                                context.Token = token;
                        }
                        else if (path.Contains("/hubs/realtime", StringComparison.OrdinalIgnoreCase))
                        {
                            var token = context.Request.Query["access_token"].FirstOrDefault();

                            if (!string.IsNullOrEmpty(token))
                                context.Token = token;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}
