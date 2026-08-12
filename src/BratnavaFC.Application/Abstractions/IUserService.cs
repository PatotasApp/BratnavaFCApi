using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Users;

namespace BratnavaFC.Application.Abstractions;

/// <summary>
/// Consultas e administração de usuários. A criação de conta acontece no primeiro acesso, via
/// <see cref="IUserProvisioningService"/> — não há endpoint de cadastro. Senha, reset e
/// verificação de e-mail são do SDK do Firebase no front-end.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Perfil do usuário autenticado, resolvido pela identidade interna.
    /// </summary>
    /// <param name="tokenEmail">
    /// E-mail vindo do ID token. O Firebase é a fonte da verdade do e-mail, então este método
    /// aproveita a chamada para alinhar a coluna quando o usuário troca o endereço lá.
    /// </param>
    Task<Result<MeDto>> GetMeAsync(Guid userId, string? tokenEmail, CancellationToken cancellationToken);

    /// <summary>Edição do próprio perfil. Não altera e-mail, role nem status.</summary>
    Task<Result<bool>> UpdateMeAsync(Guid userId, UpdateMeDto dto, CancellationToken cancellationToken);

    Task<Result<UserDto>> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result<PagedResultDto<UserListItemDto>>> GetAllAsync(ListUsersRequestDto req, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid userId, UpdateUserDto dto, CancellationToken cancellationToken);

    Task<Result<UserPhotoDto>> SetPhotoAsync(Guid userId, byte[] data, string contentType, CancellationToken cancellationToken);
    Task<Result<(byte[] Data, string ContentType, DateTimeOffset UpdatedAt)>> GetPhotoAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result> RemovePhotoAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result> InactivateAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result> ReactivateAsync(Guid userId, CancellationToken cancellationToken);
}
