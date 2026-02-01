using Microsoft.AspNetCore.Mvc;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Application.Abstractions;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MatchesController : ControllerBase
{
    private readonly IMatchService _service;

    public MatchesController(IMatchService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var matches = await _service.GetAllAsync();
        var dtos = matches.Select(ToDto);
        return Ok(dtos);
    }

    [HttpGet("{matchId:guid}")]
    public async Task<IActionResult> Get(Guid matchId)
    {
        var match = await _service.GetByIdAsync(matchId);
        if (match == null) return NotFound();
        return Ok(ToDto(match));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMatchDto dto, CancellationToken cancellationToken)
    {
        var entity = FromDto(dto);
        var created = await _service.CreateAsync(entity, cancellationToken);
        return CreatedAtAction(nameof(Get), new { matchId = created.Id }, ToDto(created));
    }

    [HttpPut("{matchId:guid}")]
    public async Task<IActionResult> Update(Guid matchId, [FromBody] UpdateMatchDto dto, CancellationToken cancellationToken)
    {
        if (dto.Id.HasValue && dto.Id.Value != matchId) return BadRequest();

        var existing = await _service.GetByIdAsync(matchId);
        if (existing == null) return NotFound();

        existing.SetPlayedAt(dto.PlayedAt);
        existing.SetPlaceName(dto.PlaceName);

        await _service.UpdateAsync(existing, cancellationToken);

        return NoContent();
    }

    [HttpDelete("{matchId:guid}")]
    public async Task<IActionResult> Delete(Guid matchId, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(matchId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{matchId:guid}/vote")]
    public async Task<IActionResult> Vote(Guid matchId, [FromBody] VoteRequestDto dto, CancellationToken cancellationToken)
    {
        if (dto == null) return BadRequest();

        if (dto.VoterPlayerId == Guid.Empty || dto.VotedPlayerId == Guid.Empty)
            return BadRequest("O jogador que votou e o votado s�o obrigat�rios.");

        if (dto.VoterPlayerId == dto.VotedPlayerId)
            return BadRequest("O jogador n�o pode votar em si mesmo.");

        try
        {
            await _service.VoteAsync(matchId, dto.VoterPlayerId, dto.VotedPlayerId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{matchId:guid}/finalize")]
    public async Task<IActionResult> Finalize(Guid matchId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.FinalizeMatchAsync(matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{matchId:guid}/mvp")]
    public async Task<IActionResult> GetMvp(Guid matchId)
    {
        var mvp = await _service.GetMvpAsync(matchId);
        if (mvp == null) return NotFound();

        var dto = new MatchPlayerDto
        {
            Id = mvp.Id,
            Name = mvp.Name,
            IsMvp = mvp.IsMvp,
        };
        return Ok(dto);
    }

    [HttpPut("{matchId:guid}/score")]
    public async Task<IActionResult> SetScore(Guid matchId, [FromBody] SetScoreRequestDto dto, CancellationToken cancellationToken)
    {
        if (dto == null) return BadRequest();
        try
        {
            await _service.SetScoreAsync(matchId, dto.TeamAGoals, dto.TeamBGoals, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static MatchDto ToDto(MatchEntity e)
    {
        return new MatchDto
        {
            PlayedAt = e.PlayedAt,
            PlaceName = e.PlaceName,
            TeamAGoals = e.TeamAGoals ?? 0,
            TeamBGoals = e.TeamBGoals ?? 0
        };
    }

    private static MatchEntity FromDto(CreateMatchDto dto)
    {
        var match = new MatchEntity(dto.PlayedAt, dto.PlaceName);

        return match;
    }
}