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
                MvpTieRule           = (int)MvpTieRule.AllMvp,
                MvpTieMaxPlayers     = 2,
                ShowPlayerStats      = false,
                ShowStatsGeneralTab = true,
                ShowStatsPerMatchTab = true,
                ShowStatsClassificationTab = true,
                PaymentDueDay        = null,
                AutoFinalizeMvpHours = null,
                MatchSchedulingEnabled = false,
                MatchSchedulingMode = 0,
                MatchScheduleDayOfWeek = null,
                MatchScheduleTime = null,
                ManualMatchSchedules = new(),
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
            ApplyStatsTabs(entity, dto);
            entity.SetPaymentDueDay(dto.PaymentDueDay);
            entity.SetAutoFinalizeMvpHours(dto.AutoFinalizeMvpHours);
            ApplyMatchScheduling(entity, dto);
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
            ApplyStatsTabs(entity, dto);
            entity.SetPaymentDueDay(dto.PaymentDueDay);
            entity.SetAutoFinalizeMvpHours(dto.AutoFinalizeMvpHours);
            ApplyMatchScheduling(entity, dto);
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
            ApplyStatsTabs(existing, dto);
            existing.SetPaymentDueDay(dto.PaymentDueDay);
            existing.SetAutoFinalizeMvpHours(dto.AutoFinalizeMvpHours);
            ApplyMatchScheduling(existing, dto);

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
        MvpTieMaxPlayers     = e.MvpTieMaxPlayers,
        ShowPlayerStats      = e.ShowPlayerStats,
        ShowStatsGeneralTab = e.ShowStatsGeneralTab,
        ShowStatsPerMatchTab = e.ShowStatsPerMatchTab,
        ShowStatsClassificationTab = e.ShowStatsClassificationTab,
        PaymentDueDay        = e.PaymentDueDay,
        AutoFinalizeMvpHours = e.AutoFinalizeMvpHours,
        MatchSchedulingEnabled = e.MatchSchedulingEnabled,
        MatchSchedulingMode = e.MatchSchedulingMode,
        MatchScheduleDayOfWeek = e.MatchScheduleDayOfWeek,
        MatchScheduleTime = e.MatchScheduleTime,
        ManualMatchSchedules = e.GetManualMatchSchedules()
            .Select(x => new ManualMatchScheduleDto
            {
                PlayedAt = x.PlayedAt,
                Created = x.Created,
                MatchId = x.MatchId,
            })
            .ToList(),
    };

    private static void ApplyStatsTabs(GroupSettingsEntity entity, UpsertGroupSettingsDto dto)
    {
        if (!dto.ShowStatsGeneralTab.HasValue &&
            !dto.ShowStatsPerMatchTab.HasValue &&
            !dto.ShowStatsClassificationTab.HasValue)
        {
            return;
        }

        entity.SetStatsTabs(
            dto.ShowStatsGeneralTab ?? entity.ShowStatsGeneralTab,
            dto.ShowStatsPerMatchTab ?? entity.ShowStatsPerMatchTab,
            dto.ShowStatsClassificationTab ?? entity.ShowStatsClassificationTab);
    }

    private static void ApplyMatchScheduling(GroupSettingsEntity entity, UpsertGroupSettingsDto dto)
    {
        if (!dto.MatchSchedulingEnabled.HasValue &&
            !dto.MatchSchedulingMode.HasValue &&
            !dto.MatchScheduleDayOfWeek.HasValue &&
            !dto.MatchScheduleTime.HasValue &&
            dto.ManualMatchSchedules is null)
        {
            return;
        }

        var replacingSchedule = dto.MatchSchedulingEnabled.HasValue ||
                                dto.MatchSchedulingMode.HasValue ||
                                dto.ManualMatchSchedules is not null;

        var manualSchedules = dto.ManualMatchSchedules?
            .Select(x => new ManualMatchScheduleEntry
            {
                PlayedAt = DateTime.SpecifyKind(x.PlayedAt, DateTimeKind.Utc),
                Created = x.Created,
                MatchId = x.MatchId,
            })
            .ToList()
            ?? entity.GetManualMatchSchedules().ToList();

        entity.SetMatchScheduling(
            dto.MatchSchedulingEnabled ?? entity.MatchSchedulingEnabled,
            (short)(dto.MatchSchedulingMode ?? entity.MatchSchedulingMode),
            replacingSchedule ? dto.MatchScheduleDayOfWeek : entity.MatchScheduleDayOfWeek,
            replacingSchedule ? dto.MatchScheduleTime : entity.MatchScheduleTime,
            manualSchedules);
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
}
