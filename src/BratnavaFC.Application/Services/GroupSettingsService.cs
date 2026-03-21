using System;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
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

    public async Task<Result<GroupSettingsDto>> GetAsync(Guid groupId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<GroupSettingsDto>.Fail(groupCheck.Error!, groupCheck.Status);

        var entity = await _context.GroupSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.GroupId == groupId, ct);

        if (entity is null)
        {
            return Result<GroupSettingsDto>.Ok(new GroupSettingsDto
            {
                GroupId = groupId,
                MinPlayers = 5,
                MaxPlayers = 6,
                DefaultPlaceName = null,
                DefaultDayOfWeek = null,
                DefaultKickoffTime = null,
                IsPersisted = false,
                GoalIcon       = null,
                GoalkeeperIcon = null,
                AssistIcon     = null,
                OwnGoalIcon    = null,
                MvpIcon        = null,
                PlayerIcon     = null,
                MonthlyFee     = null,
            });
        }

        return Result<GroupSettingsDto>.Ok(ToDto(entity, isPersisted: true));
    }

    public async Task<Result<GroupSettingsDto>> UpsertAsync(Guid groupId, UpsertGroupSettingsDto dto, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<GroupSettingsDto>.Fail(groupCheck.Error!, groupCheck.Status);

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

            entity.SetIcons(dto.GoalIcon, dto.GoalkeeperIcon, dto.AssistIcon, dto.OwnGoalIcon, dto.MvpIcon, dto.PlayerIcon);
            entity.SetMonthlyFee(dto.MonthlyFee);
            if (dto.PaymentMode.HasValue)
                entity.SetPaymentMode((PaymentMode)dto.PaymentMode.Value);
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
            entity.SetIcons(dto.GoalIcon, dto.GoalkeeperIcon, dto.AssistIcon, dto.OwnGoalIcon, dto.MvpIcon, dto.PlayerIcon);
            entity.SetMonthlyFee(dto.MonthlyFee);
            if (dto.PaymentMode.HasValue)
                entity.SetPaymentMode((PaymentMode)dto.PaymentMode.Value);
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
            existing.SetIcons(dto.GoalIcon, dto.GoalkeeperIcon, dto.AssistIcon, dto.OwnGoalIcon, dto.MvpIcon, dto.PlayerIcon);
            existing.SetMonthlyFee(dto.MonthlyFee);
            if (dto.PaymentMode.HasValue)
                existing.SetPaymentMode((PaymentMode)dto.PaymentMode.Value);

            await _context.SaveChangesAsync(ct);
            entity = existing;
        }

        return Result<GroupSettingsDto>.Ok(ToDto(entity, isPersisted: true), "GroupSettings atualizado com sucesso.");
    }


    private static GroupSettingsDto ToDto(GroupSettingsEntity e, bool isPersisted) => new()
    {
        GroupId = e.GroupId,
        MinPlayers = e.MinPlayers,
        MaxPlayers = e.MaxPlayers,
        DefaultPlaceName = e.DefaultPlaceName,
        DefaultDayOfWeek = e.DefaultDayOfWeek,
        DefaultKickoffTime = e.DefaultKickoffTime,
        IsPersisted = isPersisted,
        GoalIcon       = e.GoalIcon,
        GoalkeeperIcon = e.GoalkeeperIcon,
        AssistIcon     = e.AssistIcon,
        OwnGoalIcon    = e.OwnGoalIcon,
        MvpIcon        = e.MvpIcon,
        PlayerIcon     = e.PlayerIcon,
        PaymentMode    = (int)e.PaymentMode,
        MonthlyFee     = e.MonthlyFee,
    };

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
}
