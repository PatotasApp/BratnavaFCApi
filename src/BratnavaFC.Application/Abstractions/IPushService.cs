using BratnavaFC.Domain.Common;

namespace BratnavaFC.Application.Abstractions;

public interface IPushService
{
    /// <summary>Registra ou atualiza o token FCM de um dispositivo para o usuário autenticado.</summary>
    Task<Result> RegisterTokenAsync(Guid userId, string token, string platform, CancellationToken cancellationToken);

    /// <summary>
    /// Envia notificação para todos os dispositivos ativos de um usuário.
    /// Se groupId for fornecido, o nome do grupo é prefixado no título automaticamente.
    /// </summary>
    Task SendToUserAsync(Guid userId, string title, string body,
        Dictionary<string, string>? data = null, CancellationToken cancellationToken = default,
        Guid? groupId = null);

    /// <summary>
    /// Envia notificação para uma lista de tokens FCM específica.
    /// Se groupId for fornecido, o nome do grupo é prefixado no título automaticamente.
    /// </summary>
    Task SendToTokensAsync(IEnumerable<string> tokens, string title, string body,
        Dictionary<string, string>? data = null, CancellationToken cancellationToken = default,
        Guid? groupId = null);

    /// <summary>Envia notificação para todos os jogadores de um grupo (via tokens ativos).</summary>
    Task SendToGroupAsync(Guid groupId, string title, string body,
        Dictionary<string, string>? data = null, CancellationToken cancellationToken = default);

    /// <summary>Envia notificação para todos os admins de um grupo.</summary>
    Task SendToGroupAdminsAsync(Guid groupId, string title, string body,
        Dictionary<string, string>? data = null, CancellationToken cancellationToken = default);

    /// <summary>Envia notificação para todos os financeiros de um grupo.</summary>
    Task SendToGroupFinanceirosAsync(Guid groupId, string title, string body,
        Dictionary<string, string>? data = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envia mensagem data-only (sem campo Notification) para todos os jogadores de um grupo.
    /// Usado quando o Flutter deve exibir a notificação local com botões de ação.
    /// </summary>
    Task SendDataOnlyToGroupAsync(Guid groupId, Dictionary<string, string> data,
        CancellationToken cancellationToken = default);
}
