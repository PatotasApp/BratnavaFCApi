using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthenticationController : BaseApiController
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IUserService _userService;

    public AuthenticationController(
        IAuthenticationService authenticationService,
        IUserService userService)
    {
        _authenticationService = authenticationService;
        _userService = userService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginDto request, CancellationToken cancellationToken)
    {
        var result = await _authenticationService.LoginAsync(request, cancellationToken);
        return ToResponse(result);
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshTokenAsync([FromBody] RefreshTokenDto request, CancellationToken cancellationToken)
    {
        var result = await _authenticationService.RefreshTokenAsync(request, cancellationToken);
        return ToResponse(result);
    }

    /// <summary>
    /// Revoga o refresh token informado, invalidando-o no banco.
    /// Deve ser chamado no logout para impedir reutilização de tokens comprometidos.
    /// </summary>
    [HttpPost("revoke")]
    public async Task<IActionResult> RevokeAsync([FromBody] RefreshTokenDto request, CancellationToken cancellationToken)
    {
        await _authenticationService.RevokeTokenAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPasswordAsync([FromBody] ForgotPasswordDto request, CancellationToken cancellationToken)
    {
        var result = await _userService.RequestPasswordResetAsync(request.Email, cancellationToken);
        return ToResponse(result);
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPasswordAsync([FromBody] ResetPasswordDto request, CancellationToken cancellationToken)
    {
        var result = await _userService.ResetPasswordAsync(request, cancellationToken);
        return ToResponse(result);
    }
}
