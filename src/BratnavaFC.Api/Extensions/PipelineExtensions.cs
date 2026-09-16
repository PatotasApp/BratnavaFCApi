using BratnavaFC.Api.Auth;
using BratnavaFC.Api.Middleware;
using BratnavaFC.Application;
using BratnavaFC.Domain.Common;
using BratnavaFC.Infrastructure;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using Serilog.Events;

namespace BratnavaFC.Api.Extensions;

public static class PipelineExtensions
{
    /// <summary>
    /// Rotas de probe e a API do dashboard, excluídas do log de request: o poller do
    /// HealthChecks UI bate a cada 60s e o orquestrador sonda continuamente, o que
    /// afogaria o log em ruído.
    /// </summary>
    private static readonly string[] SilentPaths =
    [
        "/livez",
        "/readyz",
        "/healthz",
        "/health",
        "/healthchecks-api",
    ];

    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseGlobalExceptionHandler();

        app.UseSerilogRequestLogging(options =>
        {
            // Verbose fica abaixo do MinimumLevel dos dois ambientes, então na prática
            // some do log — sem precisar de filtro no sink.
            options.GetLevel = (httpContext, _, exception) =>
            {
                if (exception is not null)
                    return LogEventLevel.Error;

                if (httpContext.Response.StatusCode >= 500)
                    return LogEventLevel.Error;

                return IsSilent(httpContext.Request.Path)
                    ? LogEventLevel.Verbose
                    : LogEventLevel.Information;
            };
        });

        // Precisa casar com o gate de AddApiPresentation: sem o SwaggerGen registrado,
        // este middleware não teria de onde gerar o documento.
        if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "BratnavaFC API v1"));
        }

        // Não redireciona HTTP para HTTPS no ambiente local: permite o celular acessar
        // a API pelo IP da rede.
        if (!app.Environment.IsDevelopment())
            app.UseHttpsRedirection();

        app.UseCors("AllowAll");
        app.UseRateLimiter();

        app.UseAuthentication();

        // Entre os dois de propósito: precisa do principal já montado pelo UseAuthentication,
        // e precisa injetar a claim de role antes de o UseAuthorization avaliar os
        // [Authorize(Roles = ...)].
        app.UseMiddleware<FirebaseIdentityMiddleware>();

        app.UseAuthorization();

        app.UseMiddleware<AuditMiddleware>();

        return app;
    }

    /// <summary>
    /// Dashboard e jobs recorrentes só existem onde o Hangfire foi registrado — ver
    /// <see cref="BackgroundJobsGate"/>. O dashboard fica restrito a Production porque
    /// expõe payload e histórico de execução dos jobs.
    /// </summary>
    public static WebApplication UseBackgroundJobs(this WebApplication app)
    {
        if (!BackgroundJobsGate.IsEnabled(app.Configuration, app.Environment))
            return app;

        if (app.Environment.IsProduction())
        {
            app.UseHangfireDashboard("/hangfire", new DashboardOptions
            {
                Authorization = [new HangfireGodModeAuthFilter()]
            });
        }

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        ApplicationDependencyInjection.UseRecurringJobs(
            app.Services.GetRequiredService<IRecurringJobManager>(),
            logger);

        return app;
    }

    private static bool IsSilent(PathString path)
        => SilentPaths.Any(silent => path.StartsWithSegments(silent, StringComparison.OrdinalIgnoreCase));

    private static void UseGlobalExceptionHandler(this WebApplication app)
    {
        app.UseExceptionHandler(appError =>
        {
            appError.Run(async context =>
            {
                var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;

                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("GlobalExceptionHandler");

                // Dependência desligada no ambiente não é falha: é 503 com a razão.
                if (ex is DependencyDisabledException disabled)
                {
                    logger.LogInformation(
                        "Dependência desabilitada — {Method} {Path} {Dependency}",
                        context.Request.Method,
                        context.Request.Path,
                        disabled.Dependency);

                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.ContentType = "application/json";

                    await context.Response.WriteAsJsonAsync(
                        new ApiResponse<object>(false, null, null, disabled.Message, []));

                    return;
                }

                logger.LogError(
                    ex,
                    "Unhandled exception — {Method} {Path}",
                    context.Request.Method,
                    context.Request.Path);

                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";

                var isDev = app.Environment.IsDevelopment()
                            || app.Configuration.GetValue<bool>("Diagnostics:DetailedErrors");

                var errorMessage = isDev && ex is not null
                    ? $"[{ex.GetType().Name}] {ex.Message}"
                    : "Erro interno no servidor.";

                await context.Response.WriteAsJsonAsync(
                    new ApiResponse<object>(false, null, null, errorMessage, []));
            });
        });
    }
}
