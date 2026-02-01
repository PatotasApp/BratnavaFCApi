using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Users;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateAsync([FromBody] CreateUserDto dto, CancellationToken cancellationToken)
        {
            await _userService.CreateUserAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(CreateAsync), "created");
        }
    }
}
