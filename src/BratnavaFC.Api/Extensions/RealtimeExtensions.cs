using BratnavaFC.Api.Realtime;
using BratnavaFC.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace BratnavaFC.Api.Extensions;

public static class RealtimeExtensions
{
    /// <summary>
    /// O hub SignalR é a única dependência que de fato impede o auto-stop da máquina no Fly:
    /// é conexão inbound de longa duração atravessando o proxy, que mantém a concorrência
    /// acima de zero enquanto houver cliente conectado. Por isso não sobe em Development.
    /// </summary>
    public static IServiceCollection AddRealtime(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            services.AddScoped<IRealtimeNotifier, NoOpRealtimeNotifier>();
            return services;
        }

        services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

        // Precisa vir junto do AddSignalR: sem ele o Clients.User(...) do sininho não resolve
        // destinatário, porque o provider padrão lê ClaimTypes.NameIdentifier e este JWT usa "sub".
        services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();

        services.AddSignalR();

        return services;
    }

    public static WebApplication MapRealtimeHub(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            app.MapHub<RealtimeHub>("/hubs/realtime");

        return app;
    }
}
