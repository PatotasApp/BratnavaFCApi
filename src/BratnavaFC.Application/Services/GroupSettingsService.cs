using System;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class GroupSettingsService : IGroupSettingsService
{
    private readonly AppDbContext _context;

    public GroupSettingsService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<GroupSettingsDto> GetAsync(Guid groupId, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var entity = await _context.GroupSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.GroupId == groupId, ct);

        if (entity is null)
        {
            return new GroupSettingsDto
            {
                GroupId = groupId,
                MinPlayers = 5,
                MaxPlayers = 6,
                DefaultPlaceName = null,
                DefaultDayOfWeek = null,
                DefaultKickoffTime = null,
                IsPersisted = false
            };
        }

        return ToDto(entity, isPersisted: true);
    }

    public async Task<GroupSettingsDto> UpsertAsync(Guid groupId, UpsertGroupSettingsDto dto, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);
        ArgumentNullException.ThrowIfNull(dto);

        var entity = await _context.GroupSettings
            .FirstOrDefaultAsync(x => x.GroupId == groupId, ct);

        if (entity is null)
        {
            entity = new GroupSettingsEntity(
                groupId,
                dto.MinPlayers,
                dto.MaxPlayers,
                dto.DefaultPlaceName,
                dto.DefaultDayOfWeek,
                dto.DefaultKickoffTime);

            await _context.GroupSettings.AddAsync(entity, ct);
        }
        else
        {
            entity.Update(
                dto.MinPlayers,
                dto.MaxPlayers,
                dto.DefaultPlaceName,
                dto.DefaultDayOfWeek,
                dto.DefaultKickoffTime);
        }

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var existing = await _context.GroupSettings
                .FirstOrDefaultAsync(x => x.GroupId == groupId, ct);

            if (existing is null) throw;

            existing.Update(
                dto.MinPlayers,
                dto.MaxPlayers,
                dto.DefaultPlaceName,
                dto.DefaultDayOfWeek,
                dto.DefaultKickoffTime);

            await _context.SaveChangesAsync(ct);
            entity = existing;
        }

        return ToDto(entity, isPersisted: true);
    }


    private static GroupSettingsDto ToDto(GroupSettingsEntity e, bool isPersisted) => new()
    {
        GroupId = e.GroupId,
        MinPlayers = e.MinPlayers,
        MaxPlayers = e.MaxPlayers,
        DefaultPlaceName = e.DefaultPlaceName,
        DefaultDayOfWeek = e.DefaultDayOfWeek,
        DefaultKickoffTime = e.DefaultKickoffTime,
        IsPersisted = isPersisted
    };

    private async Task EnsureGroupExistsAsync(Guid groupId, CancellationToken ct)
    {
        if (groupId == Guid.Empty)
            throw new InvalidOperationException("GroupId é obrigatório.");

        var exists = await _context.Groups
            .AsNoTracking()
            .AnyAsync(g => g.Id == groupId, ct);

        if (!exists)
            throw new InvalidOperationException("Group não encontrado.");
    }
}
