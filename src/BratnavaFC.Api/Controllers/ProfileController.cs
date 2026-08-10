using System.Security.Claims;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class ProfileController : BaseApiController
{
    private readonly IProfileService _profile;
    public ProfileController(IProfileService profile) => _profile = profile;

    [HttpGet("user/{userId:guid}")]
    public async Task<IActionResult> GetUser(Guid userId, CancellationToken ct)
    {
        try
        {
            var data = await _profile.GetPublicProfileAsync(userId, CurrentUserId(),
                User.IsInRole("Admin") || User.IsInRole("GodMode"), ct);
            return Ok(new { data });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
    }

    [HttpGet("me/privacy")]
    public async Task<IActionResult> GetPrivacy(CancellationToken ct) =>
        Ok(new { data = await _profile.GetPrivacyAsync(CurrentUserId(), ct) });

    [HttpPut("me/privacy")]
    public async Task<IActionResult> UpdatePrivacy([FromBody] UpdateProfilePrivacyDto dto, CancellationToken ct)
    {
        try { return Ok(new { data = await _profile.UpdatePrivacyAsync(CurrentUserId(), dto, ct) }); }
        catch (ArgumentException e) { return BadRequest(new { error = e.Message }); }
    }

    private Guid CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirstValue("sub") ?? User.FindFirstValue("userId");
        return Guid.TryParse(value, out var id) ? id : throw new UnauthorizedAccessException();
    }
}
