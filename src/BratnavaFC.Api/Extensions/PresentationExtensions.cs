using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;

namespace BratnavaFC.Api.Extensions;

public static class PresentationExtensions
{
    public static IServiceCollection AddApiPresentation(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        services.AddControllers()
            .AddJsonOptions(opts =>
                opts.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter()));

        // Swagger é ferramenta de desenvolvimento. Fora de Development nem o gerador é
        // registrado — manter o SwaggerGen e o ApiExplorer na memória sem ninguém para
        // consumir só ocupa espaço e expõe a superfície da API.
        if (environment.IsDevelopment())
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerDocumentation();
        }

        services.AddPermissiveCors();
        services.AddPerUserRateLimiting();

        return services;
    }

    private static void AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "BratnavaFC API",
                Version = "v1"
            });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Informe: Bearer {seu_token}"
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });
    }

    private static void AddPermissiveCors(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithExposedHeaders(
                        "Content-Range",
                        "Accept-Ranges",
                        "Content-Length",
                        "Content-Type");
            });
        });
    }

    private static void AddPerUserRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "application/json";

                await context.HttpContext.Response.WriteAsync(
                    "{\"success\":false,\"message\":\"Muitas requisições. Tente novamente em instantes.\",\"errors\":[]}",
                    ct);
            };

            options.AddPolicy("PerUser", context =>
            {
                var userId = context.User?.FindFirstValue("sub");

                if (!string.IsNullOrEmpty(userId))
                {
                    return RateLimitPartition.GetSlidingWindowLimiter(
                        partitionKey: $"user:{userId}",
                        factory: _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = 120,
                            Window = TimeSpan.FromSeconds(60),
                            SegmentsPerWindow = 6,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0
                        });
                }

                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: $"ip:{ip}",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromSeconds(60),
                        SegmentsPerWindow = 6,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });
        });
    }
}
