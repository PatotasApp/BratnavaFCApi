using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
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
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var colors = await _service.GetAllAsync(ct);
        return Ok(colors);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var color = await _service.GetByIdAsync(id, ct);
        return color is null ? NotFound() : Ok(color);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTeamColorDto dto, CancellationToken ct)
    {
        if (dto is null) return BadRequest(new { error = "Body é obrigatório." });

        var created = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTeamColorDto dto, CancellationToken ct)
    {
        if (dto is null) return BadRequest(new { error = "Body é obrigatório." });

        await _service.UpdateAsync(id, dto, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}