using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthenticationController : ControllerBase
{
    private readonly IAuthenticationService _authenticationService;

    public AuthenticationController(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginContracts.Request request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();
        var response = await _authenticationService.LoginAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshTokenAsync([FromBody] LoginContracts.RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();
        var response = await _authenticationService.RefreshTokenAsync(request, cancellationToken);
        return Ok(response);
    }
}
