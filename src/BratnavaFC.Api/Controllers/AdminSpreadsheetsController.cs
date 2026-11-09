using System.Security.Claims;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Spreadsheets;
using BratnavaFC.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Painel das planilhas recebidas, exclusivo dos administradores da plataforma (Admin e
/// GodMode). O arquivo é privado: só sai por aqui, pela própria API, nunca por URL pública.
/// </summary>
[ApiController]
[Route("api/admin/spreadsheets")]
[Authorize(Roles = "Admin,GodMode")]
public sealed class AdminSpreadsheetsController : BaseApiController
{
    private readonly IGroupSpreadsheetService _service;

    public AdminSpreadsheetsController(IGroupSpreadsheetService service) => _service = service;

    // GET /api/admin/spreadsheets?status=0&kind=1&groupId=...
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] GroupSpreadsheetStatus? status,
        [FromQuery] GroupSpreadsheetKind? kind,
        [FromQuery] Guid? groupId,
        CancellationToken ct)
        => ToResponse(await _service.GetAllAsync(status, kind, groupId, ct));

    [HttpGet("{spreadsheetId:guid}/download")]
    public async Task<IActionResult> Download(Guid spreadsheetId, CancellationToken ct)
    {
        var result = await _service.DownloadAsync(spreadsheetId, ct);
        if (!result.Success || result.Data is null)
            return ToResponse(result);

        // Planilha não é para abrir no navegador: sempre como anexo, com o nome original.
        Response.Headers.CacheControl = "no-store";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }

    [HttpPut("{spreadsheetId:guid}/status")]
    public async Task<IActionResult> ChangeStatus(
        Guid spreadsheetId, [FromBody] ChangeGroupSpreadsheetStatusDto dto, CancellationToken ct)
        => ToResponse(await _service.ChangeStatusAsync(spreadsheetId, dto, CurrentUserId(), ct));

    [HttpDelete("{spreadsheetId:guid}")]
    public async Task<IActionResult> Delete(Guid spreadsheetId, CancellationToken ct)
        => ToResponse(await _service.DeleteAsync(spreadsheetId, ct));

    /// <summary>Identidade interna, injetada no NameIdentifier pelo FirebaseIdentityMiddleware.</summary>
    private Guid CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : throw new UnauthorizedAccessException();
    }
}
