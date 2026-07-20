using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Payments;
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
    private readonly IPaymentService _paymentService;

    public UsersController(IUserService userService, IPaymentService paymentService)
    {
        _userService = userService;
        _paymentService = paymentService;
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

    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMyAccountAsync([FromBody] DeleteAccountDto dto, CancellationToken cancellationToken)
    {
        if (dto == null) return BadRequest();

        var raw =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        if (!Guid.TryParse(raw, out var userId) || userId == Guid.Empty)
            return Unauthorized();

        var pending = await _paymentService.GetExitPendingPaymentsAsync(userId, cancellationToken);
        if (!pending.Success) return ToResponse(pending);

        if (pending.Data?.HasPending == true && !dto.ForceWithoutPayment)
            return BadRequest(new ApiResponse<ExitPendingPaymentsDto>(
                false,
                pending.Data,
                null,
                "Existem pendências financeiras antes de excluir a conta.",
                []));

        var result = await _userService.DeleteAccountAsync(userId, dto, cancellationToken);
        if (result.Success && pending.Data?.HasPending == true)
            await _paymentService.CreateExitDebtAlertsAsync(pending.Data, cancellationToken);

        return ToResponse(result);
    }

    [HttpGet("me/exit-pending")]
    public async Task<IActionResult> GetMyExitPendingPayments(CancellationToken cancellationToken)
    {
        var raw =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        if (!Guid.TryParse(raw, out var userId) || userId == Guid.Empty)
            return Unauthorized();

        var result = await _paymentService.GetExitPendingPaymentsAsync(userId, cancellationToken);
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
