using BratnavaFC.Domain.Common;

namespace BratnavaFC.Application.Abstractions;

public interface IPushService
{
    /// <summary>Registra ou atualiza o token FCM de um dispositivo para o usuário autenticado.</summary>
    Task<Result> RegisterTokenAsync(Guid userId, string token, string platform, CancellationToken cancellationToken);

    /// <summary>Envia notificação para todos os dispositivos ativos de um usuário.</summary>
    Task SendToUserAsync(Guid userId, string title, string body,
        Dictionary<string, string>? data = null, CancellationToken cancellationToken = default);

    /// <summary>Envia notificação para uma lista de tokens FCM específica.</summary>
    Task SendToTokensAsync(IEnumerable<string> tokens, string title, string body,
        Dictionary<string, string>? data = null, CancellationToken cancellationToken = default);

    /// <summary>Envia notificação para todos os jogadores de um grupo (via tokens ativos).</summary>
    Task SendToGroupAsync(Guid groupId, string title, string body,
        Dictionary<string, string>? data = null, CancellationToken cancellationToken = default);
}
