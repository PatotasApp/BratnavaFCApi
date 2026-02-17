using BratnavaFC.Domain.Dtos.Authentication;

namespace BratnavaFC.Application.Abstractions;

public interface IAuthenticationService
{
    Task<TokenDto> LoginAsync(LoginDto request, CancellationToken cancellationToken);
    Task<TokenDto> RefreshTokenAsync(RefreshTokenDto request, CancellationToken cancellationToken);
}
