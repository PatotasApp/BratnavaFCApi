using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Absences;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/absences")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class AbsencesController : GroupAuthorizedController
{
    private readonly IAbsenceService _service;
    private readonly AppDbContext _db;

    public AbsencesController(IAbsenceService service, AppDbContext db)
    {
        _service = service;
        _db      = db;
    }

    /// <summary>Lista as ausências do usuário logado.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _service.GetMineAsync(userId.Value, ct);
        return ToResponse(result);
    }

    /// <summary>Cria uma ausência para o usuário logado.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAbsenceDto dto, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _service.CreateAsync(userId.Value, dto, ct);
        return ToResponse(result);
    }

    /// <summary>Atualiza uma ausência do usuário logado.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateAbsenceDto dto, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _service.UpdateAsync(userId.Value, id, dto, ct);
        return ToResponse(result);
    }

    /// <summary>Remove uma ausência do usuário logado.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _service.DeleteAsync(userId.Value, id, ct);
        return ToResponse(result);
    }

    /// <summary>Lista ausências de todos os jogadores do grupo. Somente admins do grupo.</summary>
    [HttpGet("group/{groupId:guid}")]
    public async Task<IActionResult> GetByGroup(Guid groupId, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();

        var result = await _service.GetByGroupAsync(groupId, ct);
        return ToResponse(result);
    }
}
