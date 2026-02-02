using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Users;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateUserDto dto,
        CancellationToken cancellationToken)
    {
        if (dto == null) return BadRequest();

        await _userService.CreateUserAsync(dto, cancellationToken);

        return StatusCode(StatusCodes.Status201Created);
    }
}
