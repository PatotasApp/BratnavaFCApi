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

    public AuthenticationController(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();
        var result = await _authenticationService.LoginAsync(request, cancellationToken);
        return ToResponse(result);
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshTokenAsync([FromBody] RefreshTokenDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();
        var result = await _authenticationService.RefreshTokenAsync(request, cancellationToken);
        return ToResponse(result);
    }
}
