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

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var match = await _service.GetByIdAsync(id);
        if (match == null) return NotFound();
        return Ok(ToDto(match));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MatchDto dto)
    {
        var entity = FromDto(dto);
        var created = await _service.CreateAsync(entity);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] MatchDto dto)
    {
        if (dto.Id.HasValue && dto.Id.Value != id) return BadRequest();

        var existing = await _service.GetByIdAsync(id);
        if (existing == null) return NotFound();

        // update scalar properties
        existing.SetPlayedAt(dto.PlayedAt);
        existing.SetScore(dto.HomeGoals, dto.AwayGoals);

        // replace players: remove all existing, then add from dto
        var existingPlayers = existing.Players.ToList();
        foreach (var p in existingPlayers)
        {
            existing.RemovePlayer(p);
        }

        foreach (var pDto in dto.Players)
        {
            var player = new MatchPlayerEntity(pDto.Name);
            if (pDto.IsMvp == true) player.SetMvp(true);
            existing.AddPlayer(player);
        }

        await _service.UpdateAsync(existing);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }

    // Mapping helpers
    private static MatchDto ToDto(MatchEntity e)
    {
        return new MatchDto
        {
            Id = e.Id,
            PlayedAt = e.PlayedAt,
            HomeGoals = e.HomeGoals,
            AwayGoals = e.AwayGoals,
            CreateDate = e.CreateDate,
            UpdateDate = e.UpdateDate,
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
            if (p.IsMvp == true) player.SetMvp(true);
            match.AddPlayer(player);
        }
        return match;
    }
}