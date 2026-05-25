using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Authentication;

namespace BratnavaFC.Application.Abstractions;

public interface IAuthenticationService
{
    Task<Result<TokenDto>> LoginAsync(LoginDto request, CancellationToken cancellationToken);
    Task<Result<TokenDto>> RefreshTokenAsync(RefreshTokenDto request, CancellationToken cancellationToken);
    /// <summary>
    /// Revoga (apaga) o refresh token do banco, invalidando-o imediatamente.
    /// Chamado no logout para que tokens comprometidos não possam ser reutilizados.
    /// </summary>
    Task RevokeTokenAsync(string refreshToken, CancellationToken cancellationToken);
}
