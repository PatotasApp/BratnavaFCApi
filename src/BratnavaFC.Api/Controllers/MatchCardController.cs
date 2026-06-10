using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.MatchCard;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class MatchCardController : GroupAuthorizedController
{
    private readonly IMatchCardService _cardService;
    private readonly AppDbContext _db;

    public MatchCardController(IMatchCardService cardService, AppDbContext db)
    {
        _cardService = cardService;
        _db = db;
    }

    /// <summary>
    /// POST /api/MatchCard/group/{groupId}/generate
    /// Gera card de partida (preview ou resultado) via ChatGPT.
    /// Retorna { success: true, data: "base64..." }
    /// </summary>
    [HttpPost("group/{groupId:guid}/generate")]
    public async Task<IActionResult> GenerateCard(Guid groupId, [FromBody] GenerateMatchCardDto dto, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var result = await _cardService.GenerateCardAsync(dto, ct);
        return ToResponse(result);
    }

    /// <summary>
    /// POST /api/MatchCard/group/{groupId}/prompt
    /// Retorna o prompt que seria enviado ao OpenAI, sem gerar imagem.
    /// </summary>
    [HttpPost("group/{groupId:guid}/prompt")]
    public async Task<IActionResult> GetPrompt(Guid groupId, [FromBody] GenerateMatchCardDto dto, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(_cardService.GetPrompt(dto));
    }
}
