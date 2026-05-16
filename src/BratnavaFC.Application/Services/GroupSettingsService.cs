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
                Rank1Icon      = null,
                Rank2Icon      = null,
                Rank3Icon      = null,
                MonthlyFee           = null,
                GoalkeeperMonthlyFee = null,
                MvpTieRule       = (int)MvpTieRule.AllMvp,
                MvpTieMaxPlayers = 2,
                ShowPlayerStats  = false,
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

            entity.SetIcons(dto.GoalIcon, dto.GoalkeeperIcon, dto.AssistIcon, dto.OwnGoalIcon, dto.MvpIcon, dto.PlayerIcon, dto.Rank1Icon, dto.Rank2Icon, dto.Rank3Icon);
            entity.SetMonthlyFee(dto.MonthlyFee);
            entity.SetGoalkeeperMonthlyFee(dto.GoalkeeperMonthlyFee);
            if (dto.PaymentMode.HasValue)
                entity.SetPaymentMode((PaymentMode)dto.PaymentMode.Value);
            if (dto.MvpTieRule.HasValue)
                entity.SetMvpTieRule((MvpTieRule)dto.MvpTieRule.Value, dto.MvpTieMaxPlayers);
            if (dto.ShowPlayerStats.HasValue)
                entity.SetShowPlayerStats(dto.ShowPlayerStats.Value);
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
            entity.SetIcons(dto.GoalIcon, dto.GoalkeeperIcon, dto.AssistIcon, dto.OwnGoalIcon, dto.MvpIcon, dto.PlayerIcon, dto.Rank1Icon, dto.Rank2Icon, dto.Rank3Icon);
            entity.SetMonthlyFee(dto.MonthlyFee);
            entity.SetGoalkeeperMonthlyFee(dto.GoalkeeperMonthlyFee);
            if (dto.PaymentMode.HasValue)
                entity.SetPaymentMode((PaymentMode)dto.PaymentMode.Value);
            if (dto.MvpTieRule.HasValue)
                entity.SetMvpTieRule((MvpTieRule)dto.MvpTieRule.Value, dto.MvpTieMaxPlayers);
            if (dto.ShowPlayerStats.HasValue)
                entity.SetShowPlayerStats(dto.ShowPlayerStats.Value);
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
            existing.SetIcons(dto.GoalIcon, dto.GoalkeeperIcon, dto.AssistIcon, dto.OwnGoalIcon, dto.MvpIcon, dto.PlayerIcon, dto.Rank1Icon, dto.Rank2Icon, dto.Rank3Icon);
            existing.SetMonthlyFee(dto.MonthlyFee);
            existing.SetGoalkeeperMonthlyFee(dto.GoalkeeperMonthlyFee);
            if (dto.PaymentMode.HasValue)
                existing.SetPaymentMode((PaymentMode)dto.PaymentMode.Value);
            if (dto.MvpTieRule.HasValue)
                existing.SetMvpTieRule((MvpTieRule)dto.MvpTieRule.Value, dto.MvpTieMaxPlayers);
            if (dto.ShowPlayerStats.HasValue)
                existing.SetShowPlayerStats(dto.ShowPlayerStats.Value);

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
        Rank1Icon      = e.Rank1Icon,
        Rank2Icon      = e.Rank2Icon,
        Rank3Icon      = e.Rank3Icon,
        PaymentMode          = (int)e.PaymentMode,
        MonthlyFee           = e.MonthlyFee,
        GoalkeeperMonthlyFee = e.GoalkeeperMonthlyFee,
        MvpTieRule           = (int)e.MvpTieRule,
        MvpTieMaxPlayers = e.MvpTieMaxPlayers,
        ShowPlayerStats  = e.ShowPlayerStats,
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
