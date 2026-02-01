using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Authentication;
using BratnavaFC.Domain.Dtos.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthenticationController : ControllerBase
    {
        private readonly IAuthenticationService _authenticationService;

        public AuthenticationController(IAuthenticationService authenticationService)
        {
            _authenticationService = authenticationService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> LoginAsync([FromBody] LoginContracts.Request request, CancellationToken cancellationToken)
        {
            var response = await _authenticationService.LoginAsync(request, cancellationToken);
            return CreatedAtAction(nameof(LoginAsync), response);
        }

        
        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshTokenAsync([FromBody] LoginContracts.RefreshTokenRequest request, CancellationToken cancellationToken)
        {
            var response = await _authenticationService.RefreshTokenAsync(request, cancellationToken);
            return CreatedAtAction(nameof(LoginAsync), response);
        }
    }
}
