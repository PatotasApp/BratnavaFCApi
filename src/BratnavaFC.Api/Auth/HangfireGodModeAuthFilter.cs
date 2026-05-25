using Hangfire.Dashboard;

namespace BratnavaFC.Api.Auth;

/// <summary>
/// Restringe o painel do Hangfire (/hangfire) a usuários autenticados
/// com a role GodMode. Sem esse filtro, o dashboard fica acessível publicamente.
/// </summary>
public sealed class HangfireGodModeAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true
               && httpContext.User.IsInRole("GodMode");
    }
}
