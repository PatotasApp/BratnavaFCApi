using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BratnavaFC.Api.HealthChecks;

/// <summary>
/// Liveness (/livez e o alias legado /health): não consulta dependência nenhuma.
/// Se o Redis cair, o orquestrador não deve reiniciar o processo.
/// </summary>
internal static class LivenessResponseWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static Task WriteAsync(HttpContext httpContext, HealthReport report)
    {
        var environment = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();

        httpContext.Response.ContentType = "application/json; charset=utf-8";

        return JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            new { ok = true, environment = environment.EnvironmentName },
            Options,
            httpContext.RequestAborted);
    }
}
