using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace BratnavaFC.Api.Realtime;

/// <summary>
/// O <see cref="IUserIdProvider"/> padrão do SignalR lê <c>ClaimTypes.NameIdentifier</c>. O JWT
/// desta API carrega o id do usuário na claim <c>"sub"</c> — mesma que
/// <c>RealtimeHub.CanAccessGroupAsync</c> usa. Sem este provider, <c>Clients.User(...)</c>
/// nunca encontraria destinatário e o evento do sininho sumiria em silêncio, sem erro.
///
/// Normaliza para <c>Guid.ToString()</c> porque o SignalR casa o retorno daqui com a string
/// passada em <c>Clients.User(...)</c> por igualdade exata: sem normalizar, uma diferença de
/// caixa ou de formato entre a claim e o <c>userId.ToString()</c> do emissor quebraria a
/// entrega, também em silêncio.
/// </summary>
public sealed class SubClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
        => FromPrincipal(connection.User);

    /// <summary>
    /// A lógica vive aqui, separada da interface, porque <see cref="HubConnectionContext"/> é
    /// caro de construir em teste (exige ConnectionContext, options e ILoggerFactory). Como
    /// esta é uma peça de falha silenciosa — errar o formato faz o evento desaparecer sem
    /// exceção —, ela precisa ser testável sem cerimônia.
    /// </summary>
    public static string? FromPrincipal(ClaimsPrincipal? user)
        => Guid.TryParse(user?.FindFirstValue("sub"), out var userId)
            ? userId.ToString()
            : null;
}
