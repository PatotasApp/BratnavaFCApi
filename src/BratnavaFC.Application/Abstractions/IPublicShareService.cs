using BratnavaFC.Domain.Dtos.Share;

namespace BratnavaFC.Application.Abstractions;

public interface IPublicShareService
{
    /// <summary>Patota existente e ativa; nulo quando não existe (ou foi encerrada/inativada).</summary>
    Task<PublicGroupResponseDto?> GetGroupAsync(Guid groupId, bool isMember, CancellationToken ct = default);

    /// <summary>
    /// Patota dona do conteúdo do link (sem checar acesso). Nulo quando o conteúdo não existe
    /// ou o tipo é desconhecido.
    /// </summary>
    Task<Guid?> FindOwnerGroupAsync(string kind, Guid id, CancellationToken ct = default);

    /// <summary>Monta a resposta do link interno conforme o acesso já decidido pelo controller.</summary>
    Task<ShareTargetDto> BuildTargetAsync(string kind, Guid id, Guid groupId, string access, CancellationToken ct = default);
}
