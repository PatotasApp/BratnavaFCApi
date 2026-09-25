using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Calendar;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class CalendarController : GroupAuthorizedController
{
    private readonly ICalendarService _service;
    private readonly AppDbContext _db;

    public CalendarController(ICalendarService service, AppDbContext db)
    {
        _service = service;
        _db = db;
    }

    // ─── Eventos ─────────────────────────────────────────────────────────────

    /// <summary>Lista eventos do calendário no período [start, end]. Acessível a todos os membros.</summary>
    [HttpGet("group/{groupId:guid}")]
    public async Task<IActionResult> GetEvents(
        Guid groupId,
        [FromQuery] string? start,
        [FromQuery] string? end,
        CancellationToken ct)
    {
        if (!DateOnly.TryParse(start, out var startDate))
            return BadRequest(new { error = "Parâmetro 'start' inválido. Use YYYY-MM-DD." });
        if (!DateOnly.TryParse(end, out var endDate))
            return BadRequest(new { error = "Parâmetro 'end' inválido. Use YYYY-MM-DD." });
        if (endDate < startDate)
            return BadRequest(new { error = "'end' deve ser maior ou igual a 'start'." });

        var result = await _service.GetEventsAsync(groupId, startDate, endDate, ct);
        return ToResponse(result);
    }

    /// <summary>Cria um evento manual. Somente admins do grupo.</summary>
    [HttpPost("group/{groupId:guid}/events")]
    public async Task<IActionResult> CreateEvent(
        Guid groupId, [FromBody] CreateCalendarEventDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _service.CreateEventAsync(groupId, userId.Value, dto, ct);
        return ToResponse(result);
    }

    /// <summary>Atualiza um evento manual. Somente admins do grupo.</summary>
    [HttpPut("group/{groupId:guid}/events/{eventId:guid}")]
    public async Task<IActionResult> UpdateEvent(
        Guid groupId, Guid eventId, [FromBody] UpdateCalendarEventDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();

        var result = await _service.UpdateEventAsync(groupId, eventId, dto, ct);
        return ToResponse(result);
    }

    /// <summary>Remove permanentemente um evento. Somente admins do grupo.</summary>
    [HttpDelete("group/{groupId:guid}/events/{eventId:guid}")]
    public async Task<IActionResult> DeleteEvent(
        Guid groupId, Guid eventId, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();

        var result = await _service.DeleteEventAsync(groupId, eventId, ct);
        return ToResponse(result);
    }

    // ─── Categorias ──────────────────────────────────────────────────────────

    /// <summary>Lista categorias do grupo. Acessível a todos os membros.</summary>
    [HttpGet("group/{groupId:guid}/categories")]
    public async Task<IActionResult> GetCategories(Guid groupId, CancellationToken ct)
    {
        var result = await _service.GetCategoriesAsync(groupId, ct);
        return ToResponse(result);
    }

    /// <summary>Cria uma categoria. Somente admins do grupo.</summary>
    [HttpPost("group/{groupId:guid}/categories")]
    public async Task<IActionResult> CreateCategory(
        Guid groupId, [FromBody] CreateCalendarCategoryDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();

        var result = await _service.CreateCategoryAsync(groupId, dto, ct);
        return ToResponse(result);
    }

    /// <summary>Atualiza uma categoria. Somente admins do grupo.</summary>
    [HttpPut("group/{groupId:guid}/categories/{categoryId:guid}")]
    public async Task<IActionResult> UpdateCategory(
        Guid groupId, Guid categoryId, [FromBody] UpdateCalendarCategoryDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();

        var result = await _service.UpdateCategoryAsync(groupId, categoryId, dto, ct);
        return ToResponse(result);
    }

    /// <summary>Remove permanentemente uma categoria. Apenas GodMode.</summary>
    [HttpDelete("group/{groupId:guid}/categories/{categoryId:guid}")]
    [Authorize(Roles = "GodMode")]
    public async Task<IActionResult> DeleteCategory(
        Guid groupId, Guid categoryId, CancellationToken ct)
    {
        var result = await _service.DeleteCategoryAsync(groupId, categoryId, ct);
        return ToResponse(result);
    }
}
