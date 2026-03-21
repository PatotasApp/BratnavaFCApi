using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Authentication;

namespace BratnavaFC.Application.Abstractions;

public interface IAuthenticationService
{
    Task<Result<TokenDto>> LoginAsync(LoginDto request, CancellationToken cancellationToken);
    Task<Result<TokenDto>> RefreshTokenAsync(RefreshTokenDto request, CancellationToken cancellationToken);
}
