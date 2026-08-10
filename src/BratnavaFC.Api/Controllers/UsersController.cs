using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

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

    [HttpPost("{userId:guid}/photo")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhotoAsync(
        Guid userId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!CanManagePhoto(userId)) return Forbid();
        if (file is null || file.Length == 0)
            return BadRequest("Selecione uma foto.");
        if (file.Length > 5 * 1024 * 1024)
            return BadRequest("A foto deve ter no máximo 5 MB.");

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, cancellationToken);

        var result = await _userService.SetPhotoAsync(
            userId,
            buffer.ToArray(),
            file.ContentType,
            cancellationToken);
        return ToResponse(result);
    }

    [AllowAnonymous]
    [HttpGet("{userId:guid}/photo")]
    public async Task<IActionResult> GetPhotoAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _userService.GetPhotoAsync(userId, cancellationToken);
        if (!result.Success || result.Data == default)
            return ToResponse(result);

        var (data, contentType, updatedAt) = result.Data;
        Response.Headers.CacheControl = "public,max-age=3600,must-revalidate";
        Response.Headers.ETag = $"\"{updatedAt.ToUnixTimeMilliseconds()}\"";
        Response.Headers.LastModified = updatedAt.ToString("R");
        return File(data, contentType);
    }

    [HttpDelete("{userId:guid}/photo")]
    public async Task<IActionResult> DeletePhotoAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!CanManagePhoto(userId)) return Forbid();
        var result = await _userService.RemovePhotoAsync(userId, cancellationToken);
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

    private bool CanManagePhoto(Guid userId)
    {
        if (User.IsInRole("Admin") || User.IsInRole("GodMode")) return true;

        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? User.FindFirstValue("userId");
        return Guid.TryParse(raw, out var currentUserId) && currentUserId == userId;
    }
}
