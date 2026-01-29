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
    public async Task<IActionResult> Create([FromBody] MatchDto dto)
    {
        var entity = FromDto(dto);
        var created = await _service.CreateAsync(entity);
        return CreatedAtAction(nameof(Get), new { matchId = created.Id }, ToDto(created));
    }

    [HttpPut("{matchId:guid}")]
    public async Task<IActionResult> Update(Guid matchId, [FromBody] MatchDto dto)
    {
        if (dto.Id.HasValue && dto.Id.Value != matchId) return BadRequest();

        var existing = await _service.GetByIdAsync(matchId);
        if (existing == null) return NotFound();

        existing.SetPlayedAt(dto.PlayedAt);
        existing.SetScore(dto.HomeGoals, dto.AwayGoals);

        var existingPlayers = existing.Players.ToList();
        foreach (var p in existingPlayers)
        {
            existing.RemovePlayer(p);
        }

        foreach (var pDto in dto.Players)
        {
            var player = new MatchPlayerEntity(pDto.Name);
            if (pDto.IsMvp == true) player.SetMvp();
            existing.AddPlayer(player);
        }

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

        var dto = new MatchPlayerDto
        {
            Id = mvp.Id,
            Name = mvp.Name,
            IsMvp = mvp.IsMvp,
        };
        return Ok(dto);
    }

    private static MatchDto ToDto(MatchEntity e)
    {
        return new MatchDto
        {
            Id = e.Id,
            PlayedAt = e.PlayedAt,
            HomeGoals = e.TeamAGoals,
            AwayGoals = e.TeamBGoals,
            Players = e.Players.Select(p => new MatchPlayerDto
            {
                Id = p.Id,
                Name = p.Name,
                IsMvp = p.IsMvp
            }).ToList()
        };
    }

    private static MatchEntity FromDto(MatchDto dto)
    {
        var match = new MatchEntity(dto.PlayedAt);
        match.SetScore(dto.HomeGoals, dto.AwayGoals);
        foreach (var p in dto.Players)
        {
            var player = new MatchPlayerEntity(p.Name);
            if (p.IsMvp == true) player.SetMvp();
            match.AddPlayer(player);
        }
        return match;
    }
}