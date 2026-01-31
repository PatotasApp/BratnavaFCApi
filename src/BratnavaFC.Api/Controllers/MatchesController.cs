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
    public async Task<IActionResult> Create([FromBody] CreateMatchDto dto)
    {
        var entity = FromDto(dto);
        var created = await _service.CreateAsync(entity);
        return CreatedAtAction(nameof(Get), new { matchId = created.Id }, ToDto(created));
    }

    [HttpPut("{matchId:guid}")]
    public async Task<IActionResult> Update(Guid matchId, [FromBody] UpdateMatchDto dto)
    {
        if (dto.Id.HasValue && dto.Id.Value != matchId) return BadRequest();

        var existing = await _service.GetByIdAsync(matchId);
        if (existing == null) return NotFound();

        existing.SetPlayedAt(dto.PlayedAt);
        existing.SetPlaceName(dto.PlaceName);

        await _service.UpdateAsync(existing);

        return NoContent();
    }

    [HttpDelete("{matchId:guid}")]
    public async Task<IActionResult> Delete(Guid matchId)
    {
        await _service.DeleteAsync(matchId);
        return NoContent();
    }

    [HttpPost("{matchId:guid}/vote")]
    public async Task<IActionResult> Vote(Guid matchId, [FromBody] VoteRequestDto dto)
    {
        if (dto == null) return BadRequest();

        if (dto.VoterPlayerId == Guid.Empty || dto.VotedPlayerId == Guid.Empty)
            return BadRequest("O jogador que votou e o votado são obrigatórios.");

        if (dto.VoterPlayerId == dto.VotedPlayerId)
            return BadRequest("O jogador não pode votar em si mesmo.");

        try
        {
            await _service.VoteAsync(matchId, dto.VoterPlayerId, dto.VotedPlayerId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{matchId:guid}/finalize")]
    public async Task<IActionResult> Finalize(Guid matchId)
    {
        try
        {
            await _service.FinalizeMatchAsync(matchId);
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

        var dto = new MatchPlayerDto(mvp.Id, mvp.Name, mvp.IsMvp);
        
        return Ok(dto);
    }

    [HttpPut("{matchId:guid}/score")]
    public async Task<IActionResult> SetScore(Guid matchId, [FromBody] SetScoreRequestDto dto)
    {
        if (dto == null) return BadRequest();
        try
        {
            await _service.SetScoreAsync(matchId, dto.TeamAGoals, dto.TeamBGoals);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{matchId:guid}/colors")]
    public async Task<IActionResult> SetMatchColors(Guid matchId, [FromBody] SetMatchColorsRequestDto dto)
    {
        if (dto == null) return BadRequest();

        try
        {
            await _service.SetTeamColorsAsync(matchId, dto.TeamAColorId, dto.TeamBColorId, dto.Randomize);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static MatchDto ToDto(MatchEntity e) =>
        new(e.PlayedAt, e.TeamAGoals ?? 0, e.TeamBGoals ?? 0, e.PlaceName, e.TeamAColorId, e.TeamBColorId);

    private static MatchEntity FromDto(CreateMatchDto dto) =>
        new(dto.PlayedAt, dto.PlaceName);
}