using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Lado da patota das planilhas para importação: o administrador da patota envia e
/// acompanha a situação. Não há importação automática — quem importa é um administrador
/// da plataforma, pelo painel (<see cref="AdminSpreadsheetsController"/>).
/// </summary>
/// <remarks>
/// As respostas usam o envelope com status do BaseApiController (overrideSuccessStatus
/// explícito): o ToResponse de um argumento do GroupAuthorizedController devolveria 400
/// para tudo, inclusive 404/409 que o app precisa distinguir.
/// </remarks>
[ApiController]
[Route("api/groups/{groupId:guid}/spreadsheets")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class GroupSpreadsheetsController : GroupAuthorizedController
{
    private readonly IGroupSpreadsheetService _service;
    private readonly AppDbContext _db;

    public GroupSpreadsheetsController(IGroupSpreadsheetService service, AppDbContext db)
    {
        _service = service;
        _db = db;
    }

    /// <summary>multipart/form-data: file, kind (0 = jogos, 1 = pagamentos), note (opcional).</summary>
    [HttpPost]
    [RequestSizeLimit(SpreadsheetFileRules.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = SpreadsheetFileRules.MaxRequestBytes)]
    public async Task<IActionResult> Upload(
        Guid groupId,
        IFormFile? file,
        [FromForm] GroupSpreadsheetKind kind,
        [FromForm] string? note,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        if (file is null || file.Length == 0) return BadRequest(new { error = "Selecione um arquivo." });

        await using var stream = file.OpenReadStream();
        var result = await _service.UploadAsync(groupId, userId.Value, kind, file.FileName, file.Length, stream, note, ct);
        return ToResponse(result, overrideSuccessStatus: null);
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid groupId, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.GetForGroupAsync(groupId, ct), overrideSuccessStatus: null);
    }

    /// <summary>Desistir do envio: só enquanto a planilha não entrou em andamento.</summary>
    [HttpDelete("{spreadsheetId:guid}")]
    public async Task<IActionResult> Delete(Guid groupId, Guid spreadsheetId, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.DeleteForGroupAsync(groupId, spreadsheetId, ct), overrideSuccessStatus: null);
    }
}
