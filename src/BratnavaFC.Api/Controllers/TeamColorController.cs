using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TeamColorsController : ControllerBase
{
    private readonly ITeamColorService _service;

    public TeamColorsController(ITeamColorService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var colors = await _service.GetAllAsync(cancellationToken);
        return Ok(colors);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var color = await _service.GetByIdAsync(id, cancellationToken);
        if (color == null) return NotFound();
        return Ok(color);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTeamColorDto dto, CancellationToken cancellationToken)
    {
        if (dto == null) return BadRequest();
        var entity = new TeamColorEntity(dto.Name, dto.HexValue);
        var created = await _service.CreateAsync(entity, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTeamColorDto dto, CancellationToken cancellationToken)
    {
        if (dto == null) return BadRequest();
        var existing = await _service.GetByIdAsync(id, cancellationToken);
        if (existing == null) return NotFound();

        existing.SetName(dto.Name);
        existing.SetHexValue(dto.HexValue);

        await _service.UpdateAsync(existing, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}