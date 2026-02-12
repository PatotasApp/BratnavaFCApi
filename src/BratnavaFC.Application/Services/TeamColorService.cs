using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class TeamColorService : ITeamColorService
{
    private readonly AppDbContext _context;

    public TeamColorService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TeamColorDto>> GetAllAsync(Guid groupId, bool includeInactive, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        IQueryable<TeamColorEntity> query = _context.TeamColors.AsNoTracking().Where(c => c.GroupId == groupId);

        if (includeInactive)
            query = query.IgnoreQueryFilters();

        return await query
            .OrderBy(c => c.Name)
            .Select(ToDtoExpr())
            .ToListAsync(ct);
    }

    public async Task<TeamColorDto> GetByIdAsync(Guid groupId, Guid colorId, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var entity = await _context.TeamColors
            .AsNoTracking()
            .IgnoreQueryFilters() // para poder achar mesmo inativa e retornar mensagem clara
            .FirstOrDefaultAsync(c => c.GroupId == groupId && c.Id == colorId, ct);

        if (entity is null)
            throw new InvalidOperationException("Cor do time nao encontrada para este grupo.");

        return ToDto(entity);
    }

    public async Task<TeamColorDto> CreateAsync(CreateTeamColorDto dto, CancellationToken ct)
    {
        if (dto is null) throw new InvalidOperationException("Payload invalido.");
        await EnsureGroupExistsAsync(dto.GroupId, ct);

        var entity = new TeamColorEntity(dto.GroupId, dto.Name, dto.HexValue);

        await _context.TeamColors.AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);

        return ToDto(entity);
    }

    public async Task<TeamColorDto> UpdateAsync(Guid groupId, Guid colorId, UpdateTeamColorDto dto, CancellationToken ct)
    {
        if (dto is null) throw new InvalidOperationException("Payload invalido.");
        await EnsureGroupExistsAsync(groupId, ct);

        var entity = await _context.TeamColors
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.GroupId == groupId && c.Id == colorId, ct);

        if (entity is null)
            throw new InvalidOperationException("Cor do time nao encontrada para este grupo.");

        entity.SetName(dto.Name);
        entity.SetHexValue(dto.HexValue);

        await _context.SaveChangesAsync(ct);

        return ToDto(entity);
    }

    public async Task InactivateAsync(Guid groupId, Guid colorId, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var entity = await _context.TeamColors
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.GroupId == groupId && c.Id == colorId, ct);

        if (entity is null)
            throw new InvalidOperationException("Cor do time nao encontrada para este grupo.");

        entity.Inactivate();
        await _context.SaveChangesAsync(ct);
    }

    public async Task ActivateAsync(Guid groupId, Guid colorId, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var entity = await _context.TeamColors
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.GroupId == groupId && c.Id == colorId, ct);

        if (entity is null)
            throw new InvalidOperationException("Cor do time nao encontrada para este grupo.");

        entity.Activate();
        await _context.SaveChangesAsync(ct);
    }

    private async Task EnsureGroupExistsAsync(Guid groupId, CancellationToken ct)
    {
        if (groupId == Guid.Empty)
            throw new InvalidOperationException("GroupId e obrigatorio.");

        var exists = await _context.Groups
            .AsNoTracking()
            .AnyAsync(g => g.Id == groupId, ct);

        if (!exists)
            throw new InvalidOperationException("Group nao encontrado.");
    }

    private static TeamColorDto ToDto(TeamColorEntity e) => new()
    {
        Id = e.Id,
        GroupId = e.GroupId,
        IsActive = e.IsActive,
        Name = e.Name,
        HexValue = e.HexValue
    };

    private static System.Linq.Expressions.Expression<Func<TeamColorEntity, TeamColorDto>> ToDtoExpr() =>
        e => new TeamColorDto
        {
            Id = e.Id,
            GroupId = e.GroupId,
            IsActive = e.IsActive,
            Name = e.Name,
            HexValue = e.HexValue
        };
}
