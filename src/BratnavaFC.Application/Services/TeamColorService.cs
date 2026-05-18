using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
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

    public async Task<Result<IReadOnlyList<TeamColorDto>>> GetAllAsync(Guid groupId, bool activeOnly, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success)
            return Result<IReadOnlyList<TeamColorDto>>.Fail(groupCheck.Error!, groupCheck.Status);

        IQueryable<TeamColorEntity> query = _context.TeamColors.AsNoTracking().Where(c => c.GroupId == groupId);

        if (activeOnly)
            query = query.Where(c => c.IsActive);

        var list = await query
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.Name)
            .Select(ToDtoExpr())
            .ToListAsync(ct);

        return Result<IReadOnlyList<TeamColorDto>>.Ok(list);
    }

    public async Task<Result<TeamColorDto>> GetByIdAsync(Guid groupId, Guid colorId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success)
            return Result<TeamColorDto>.Fail(groupCheck.Error!, groupCheck.Status);

        var entity = await _context.TeamColors
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.GroupId == groupId && c.Id == colorId, ct);

        if (entity is null)
            return Result<TeamColorDto>.Fail("Cor do time nao encontrada para este grupo.", ResultStatus.NotFound);

        return Result<TeamColorDto>.Ok(ToDto(entity));
    }

    public async Task<Result<TeamColorDto>> CreateAsync(CreateTeamColorDto dto, CancellationToken ct)
    {
        if (dto is null)
            return Result<TeamColorDto>.Fail("Payload invalido.", ResultStatus.BadRequest);

        var groupCheck = await EnsureGroupExistsAsync(dto.GroupId, ct);
        if (!groupCheck.Success)
            return Result<TeamColorDto>.Fail(groupCheck.Error!, groupCheck.Status);

        var entity = new TeamColorEntity(dto.GroupId, dto.Name, dto.HexValue);

        await _context.TeamColors.AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);

        return Result<TeamColorDto>.Ok(ToDto(entity), "Cor do time criada com sucesso.", ResultStatus.Created);
    }

    public async Task<Result<TeamColorDto>> UpdateAsync(Guid groupId, Guid colorId, UpdateTeamColorDto dto, CancellationToken ct)
    {
        if (dto is null)
            return Result<TeamColorDto>.Fail("Payload invalido.", ResultStatus.BadRequest);

        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success)
            return Result<TeamColorDto>.Fail(groupCheck.Error!, groupCheck.Status);

        var entity = await _context.TeamColors
            .FirstOrDefaultAsync(c => c.GroupId == groupId && c.Id == colorId, ct);

        if (entity is null)
            return Result<TeamColorDto>.Fail("Cor do time nao encontrada para este grupo.", ResultStatus.NotFound);

        entity.SetName(dto.Name);
        entity.SetHexValue(dto.HexValue);

        await _context.SaveChangesAsync(ct);

        return Result<TeamColorDto>.Ok(ToDto(entity), "Cor do time atualizada com sucesso.");
    }

    public async Task<Result> InactivateAsync(Guid groupId, Guid colorId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success)
            return Result.Fail(groupCheck.Error!, groupCheck.Status);

        var entity = await _context.TeamColors
            .FirstOrDefaultAsync(c => c.GroupId == groupId && c.Id == colorId, ct);

        if (entity is null)
            return Result.Fail("Cor do time nao encontrada para este grupo.", ResultStatus.NotFound);

        entity.Inactivate();
        await _context.SaveChangesAsync(ct);

        return Result.Ok("Cor do time removida com sucesso.");
    }

    public async Task<Result> ActivateAsync(Guid groupId, Guid colorId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success)
            return Result.Fail(groupCheck.Error!, groupCheck.Status);

        var entity = await _context.TeamColors
            .FirstOrDefaultAsync(c => c.GroupId == groupId && c.Id == colorId, ct);

        if (entity is null)
            return Result.Fail("Cor do time nao encontrada para este grupo.", ResultStatus.NotFound);

        entity.Activate();
        await _context.SaveChangesAsync(ct);

        return Result.Ok("Cor do time atualizada com sucesso.");
    }

    public async Task<Result> DeleteAsync(Guid groupId, Guid colorId, CancellationToken ct)
    {
        var entity = await _context.TeamColors
            .FirstOrDefaultAsync(c => c.GroupId == groupId && c.Id == colorId, ct);

        if (entity is null)
            return Result.Fail("Cor do time não encontrada para este grupo.", ResultStatus.NotFound);

        _context.TeamColors.Remove(entity);
        await _context.SaveChangesAsync(ct);

        return Result.Ok("Cor removida permanentemente.");
    }

    private async Task<Result> EnsureGroupExistsAsync(Guid groupId, CancellationToken ct)
    {
        if (groupId == Guid.Empty)
            return Result.Fail("GroupId e obrigatorio.", ResultStatus.BadRequest);

        var exists = await _context.Groups
            .AsNoTracking()
            .AnyAsync(g => g.Id == groupId, ct);

        if (!exists)
            return Result.Fail("Group nao encontrado.", ResultStatus.NotFound);

        return Result.Ok();
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
