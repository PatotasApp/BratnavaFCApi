using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Votação do Puskas pelo QR code do telão: sem login. O acesso é pelo código secreto da votação;
/// só expõe miniaturas, autor e data dos gols e recebe até 3 escolhas por nome.
/// </summary>
[ApiController]
[Route("api/public/puskas/{code}")]
[AllowAnonymous]
[EnableRateLimiting("PerUser")]
public sealed class PublicPuskasController : ControllerBase
{
    private readonly IPuskasVotingService _voting;

    public PublicPuskasController(IPuskasVotingService voting) => _voting = voting;

    [HttpGet]
    public async Task<IActionResult> GetSheet([FromRoute] string code, CancellationToken ct)
    {
        var result = await _voting.GetSheetAsync(code, ct);
        return result.Success ? Ok(new { success = true, data = result.Data }) : NotFound(new { success = false, message = result.Error });
    }

    [HttpPost("vote")]
    public async Task<IActionResult> Vote([FromRoute] string code, [FromBody] SubmitPuskasVoteRequest request, CancellationToken ct)
    {
        var result = await _voting.SubmitVoteAsync(code, request.VoterName, request.ClipIds ?? [], ct);
        if (result.Success) return Ok(new { success = true, message = result.Message });
        var status = result.Status switch
        {
            ResultStatus.NotFound => StatusCodes.Status404NotFound,
            ResultStatus.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return StatusCode(status, new { success = false, message = result.Error });
    }
}
