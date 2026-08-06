using System.Security.Claims;
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

    /// <summary>
    /// Perfil interno do usuário autenticado. O cadastro não tem endpoint: a conta é criada no
    /// Firebase pelo front-end e provisionada aqui no primeiro acesso autenticado.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMeAsync(CancellationToken cancellationToken)
    {
        if (!TryGetInternalUserId(out var userId))
            return Unauthorized();

        var result = await _userService.GetMeAsync(userId, cancellationToken);
        return ToResponse(result);
    }

    /// <summary>
    /// Edita o próprio perfil. Um campo "email" no payload é ignorado — e-mail é read-only,
    /// gerenciado no Firebase.
    /// </summary>
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMeAsync(
        [FromBody] UpdateMeDto dto,
        CancellationToken cancellationToken)
    {
        if (dto == null) return BadRequest();

        if (!TryGetInternalUserId(out var userId))
            return Unauthorized();

        var result = await _userService.UpdateMeAsync(userId, dto, cancellationToken);
        return ToResponse(result);
    }

    /// <summary>
    /// Identidade INTERNA, injetada pelo FirebaseIdentityMiddleware. Nunca é o UID do Firebase.
    /// </summary>
    private bool TryGetInternalUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

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
