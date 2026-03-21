using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class UsersController : BaseApiController
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateUserDto dto, CancellationToken cancellationToken)
    {
        if (dto == null) return BadRequest();

        var result = await _userService.CreateUserAsync(dto, cancellationToken);
        return ToResponse(result, overrideSuccessStatus: 201);
    }

    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _userService.GetUserByIdAsync(userId, cancellationToken);
        return ToResponse(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] string? search,
        [FromQuery] Status? status,
        [FromQuery] int? role,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var req = new ListUsersRequestDto
        {
            Search = search,
            Status = status,
            Role = role,
            Page = page,
            PageSize = pageSize,
            IncludeInactive = includeInactive
        };

        var result = await _userService.GetAllAsync(req, cancellationToken);
        return ToResponse(result);
    }

    [HttpPut("{userId:guid}")]
    public async Task<IActionResult> UpdateAsync(Guid userId, [FromBody] UpdateUserDto dto, CancellationToken cancellationToken)
    {
        if (dto == null) return BadRequest();

        var result = await _userService.UpdateAsync(userId, dto, cancellationToken);
        return ToResponse(result);
    }

    [HttpPut("{userId:guid}/password")]
    public async Task<IActionResult> ChangePasswordAsync(Guid userId, [FromBody] ChangePasswordDto dto, CancellationToken cancellationToken)
    {
        if (dto == null) return BadRequest();

        var result = await _userService.ChangePasswordAsync(userId, dto, cancellationToken);
        return ToResponse(result);
    }

    [HttpPut("{userId:guid}/inactivate")]
    public async Task<IActionResult> InactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _userService.InactivateAsync(userId, cancellationToken);
        return ToResponse(result);
    }

    [HttpPut("{userId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _userService.ReactivateAsync(userId, cancellationToken);
        return ToResponse(result);
    }
}
