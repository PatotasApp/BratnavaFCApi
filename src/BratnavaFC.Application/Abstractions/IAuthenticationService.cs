using System;
using BratnavaFC.Domain.Dtos.Authentication;

namespace BratnavaFC.Application.Abstractions;

public interface IAuthenticationService
{
    Task<LoginContracts.Response> LoginAsync(LoginContracts.Request request, CancellationToken cancellationToken);
    Task<LoginContracts.Response> RefreshTokenAsync(LoginContracts.RefreshTokenRequest request, CancellationToken cancellationToken);
}
