using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Constants;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class MatchService : IMatchService
{
    private readonly AppDbContext _context;
    private readonly IRepositoryBase<MatchEntity> _repository;
    private readonly IPushService _push;
    private readonly IReplayUrlService _replayUrls;
    private readonly IBetService _bets;
    private readonly INotificationScheduler _scheduler;

    public MatchService(
        AppDbContext context,
        IRepositoryBase<MatchEntity> repository,
        IPushService push,
        IReplayUrlService replayUrls,
        IBetService bets,
        INotificationScheduler scheduler)
    {
        _context    = context;
        _repository = repository;
        _push       = push;
        _replayUrls = replayUrls;
        _bets       = bets;
        _scheduler  = scheduler;
    }

    public async Task<Result<List<MatchDetailsDto>>> GetAllAsync(Guid groupId, CancellationToken ct = default)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<List<MatchDetailsDto>>.Fail(groupCheck.Error!, groupCheck.Status);

        var matches = await _context.Matches
            .AsSplitQuery()
            .AsNoTrackingWithIdentityResolution()
            .Where(m => m.GroupId == groupId)
            .Include(m => m.Group)
            .Include(m => m.TeamAColor)
            .Include(m => m.TeamBColor)
            .Include(m => m.Goals)
            .Include(m => m.Players).ThenInclude(mp => mp.Player)
            .Include(m => m.Players).ThenInclude(mp => mp.GoalsScored)
            .Include(m => m.Players).ThenInclude(mp => mp.GoalsAssisted)
            .Include(m => m.Votes).ThenInclude(v => v.Voter)
            .Include(m => m.Votes).ThenInclude(v => v.VotedFor)
            .OrderByDescending(m => m.PlayedAt)
            .ToListAsync(ct);

        return Result<List<MatchDetailsDto>>.Ok(matches.Select(MapToDetailsDto).ToList());
    }

    public async Task<Result<MatchEntity>> GetByIdAsync(Guid groupId, Guid matchId, CancellationToken ct = default)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<MatchEntity>.Fail(groupCheck.Error!, groupCheck.Status);

        var match = await _context.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Include(m => m.Players).ThenInclude(mp => mp.Player)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(ct);

        if (match is null)
            return Result<MatchEntity>.Fail("Partida não encontrada.", ResultStatus.NotFound);

        return Result<MatchEntity>.Ok(match);
    }

    public async Task<Result<List<GoalDto>>> GetGoalsAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<List<GoalDto>>.Fail(groupCheck.Error!, groupCheck.Status);

        var matchIdCheck = EnsureMatchId(matchId);
        if (!matchIdCheck.Success) return Result<List<GoalDto>>.Fail(matchIdCheck.Error!, matchIdCheck.Status);

        var match = await _context.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Include(m => m.Goals)
            .Include(m => m.Players).ThenInclude(mp => mp.Player)
            .FirstOrDefaultAsync(ct);

        if (match is null)
            return Result<List<GoalDto>>.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var mpById = match.Players.ToDictionary(x => x.Id, x => x);

        var goals = match.Goals
            .OrderBy(g => g.TimeSeconds ?? int.MaxValue)
            .ThenBy(g => g.CreateDate)
            .Select(g =>
            {
                mpById.TryGetValue(g.ScorerMatchPlayerId, out var scorerMp);
                MatchPlayerEntity? assistMp = null;

                if (g.AssistMatchPlayerId.HasValue)
                    mpById.TryGetValue(g.AssistMatchPlayerId.Value, out assistMp);

                return new GoalDto
                {
                    GoalId = g.Id,

                    ScorerMatchPlayerId = g.ScorerMatchPlayerId,
                    ScorerPlayerId = scorerMp?.PlayerId ?? Guid.Empty,
                    ScorerName = scorerMp?.Player?.Name ?? string.Empty,

                    AssistMatchPlayerId = g.AssistMatchPlayerId,
                    AssistPlayerId = assistMp?.PlayerId,
                    AssistName = assistMp?.Player?.Name,

                    TimeSeconds = g.TimeSeconds,
                    Time = MatchTimeParser.FormatFromSeconds(g.TimeSeconds),
                    IsOwnGoal = g.IsOwnGoal
                };
            })
            .ToList();

        return Result<List<GoalDto>>.Ok(goals);
    }

    public async Task<Result<MatchDetailsDto>> GetDetailsAsync(Guid matchId, CancellationToken ct)
    {
        var match = await _context.Matches
            .AsNoTracking()
            .AsSplitQuery()
            .Include(m => m.Group)
            .Include(m => m.TeamAColor)
            .Include(m => m.TeamBColor)
            .Include(m => m.Goals)
            .Include(m => m.Players).ThenInclude(mp => mp.Player)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);

        if (match is null)
            return Result<MatchDetailsDto>.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var computedMvps = match.GetComputedMvps()
            .Select(p => new MatchMvpDto
            {
                MatchPlayerId = p.Id,
                PlayerId = p.PlayerId,
                PlayerName = p.Player?.Name ?? string.Empty,
                Team = p.Team
            })
            .ToList();

        var mpNameById = match.Players.ToDictionary(
            p => p.Id,
            p => p.Player?.Name ?? string.Empty
        );

        var teamAPlayers = match.Players.Where(p => p.Team == 1).ToList();
        var teamBPlayers = match.Players.Where(p => p.Team == 2).ToList();
        var unassigned   = match.Players.Where(p => p.Team == 0 && p.InviteResponse == InviteResponse.Accepted).ToList();

        var playerNameByPlayerId = match.Players.ToDictionary(
            p => p.PlayerId,
            p => p.Player?.Name ?? string.Empty
        );

        var mpById = match.Players.ToDictionary(p => p.Id);
        var nameByMatchPlayerId = match.Players.ToDictionary(p => p.Id, p => p.Player?.Name ?? string.Empty);

        var goals = match.Goals
            .OrderBy(g => g.TimeSeconds ?? int.MaxValue)
            .ThenBy(g => g.CreateDate)
            .Select(g =>
            {
                mpById.TryGetValue(g.ScorerMatchPlayerId, out var scorerMp);
                MatchPlayerEntity? assistMp = null;
                if (g.AssistMatchPlayerId.HasValue)
                    mpById.TryGetValue(g.AssistMatchPlayerId.Value, out assistMp);

                return new GoalDto
                {
                    GoalId = g.Id,

                    ScorerMatchPlayerId = g.ScorerMatchPlayerId,
                    AssistMatchPlayerId = g.AssistMatchPlayerId,

                    ScorerPlayerId = scorerMp?.PlayerId ?? Guid.Empty,
                    ScorerName = scorerMp?.Player?.Name ?? string.Empty,

                    AssistPlayerId = assistMp?.PlayerId,
                    AssistName = assistMp?.Player?.Name,

                    TimeSeconds = g.TimeSeconds,
                    Time = MatchTimeParser.FormatFromSeconds(g.TimeSeconds),
                    IsOwnGoal = g.IsOwnGoal
                };
            })
            .ToList();

        var voteCounts = match.Votes
            .Where(v => v.VotedForId != Guid.Empty)
            .GroupBy(v => v.VotedForId)
            .Select(g => new VoteCountDto
            {
                VotedForMatchPlayerId = g.Key,
                VotedForName = mpNameById.TryGetValue(g.Key, out var n) ? n : string.Empty,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.VotedForName)
            .ToList();

        var dto = new MatchDetailsDto
        {
            MatchId = match.Id,
            GroupName = match.Group?.Name ?? string.Empty,
            GroupId = match.GroupId,
            PlayedAt = match.PlayedAt,
            PlaceName = match.PlaceName,

            Status = (short)match.Status,
            StatusName = match.Status.ToString(),

            TeamAGoals = match.TeamAGoals,
            TeamBGoals = match.TeamBGoals,

            TeamAColor = match.TeamAColorId is null || match.TeamAColor is null
                ? null
                : new TeamColorDto
                {
                    Id = match.TeamAColor.Id,
                    Name = match.TeamAColor.Name,
                    HexValue = match.TeamAColor.HexValue
                },

            TeamBColor = match.TeamBColorId is null || match.TeamBColor is null
                ? null
                : new TeamColorDto
                {
                    Id = match.TeamBColor.Id,
                    Name = match.TeamBColor.Name,
                    HexValue = match.TeamBColor.HexValue
                },

            ComputedMvps = computedMvps,

            TeamAPlayers = teamAPlayers.Select(ToPlayerDto).OrderBy(x => x.PlayerName).ToList(),
            TeamBPlayers = teamBPlayers.Select(ToPlayerDto).OrderBy(x => x.PlayerName).ToList(),
            UnassignedPlayers = unassigned.Select(ToPlayerDto).OrderBy(x => x.PlayerName).ToList(),

            Votes = match.Votes.Select(v => new VoteDto
            {
                VoteId = v.Id,
                VoterMatchPlayerId = v.VoterId,
                VotedForMatchPlayerId = v.VotedForId,
                VoterName = mpNameById.TryGetValue(v.VoterId, out var voterName) ? voterName : string.Empty,
                VotedForName = mpNameById.TryGetValue(v.VotedForId, out var votedName) ? votedName : string.Empty
            }).ToList(),

            VoteCounts = voteCounts,
            Goals = goals,

            LinkedPollId = match.LinkedPollId,
        };

        return Result<MatchDetailsDto>.Ok(dto);
    }

    public async Task<Result<MatchEntity>> Create(Guid groupId, MatchEntity match, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<MatchEntity>.Fail(groupCheck.Error!, groupCheck.Status);

        var activeMatchCount = await _context.Matches
            .AsNoTracking()
            .CountAsync(m => m.GroupId == groupId && m.Status != MatchStatus.Finalized, ct);

        if (activeMatchCount >= MatchConstants.MaxSimultaneousActiveMatches)
            return Result<MatchEntity>.Fail(
                $"Limite de {MatchConstants.MaxSimultaneousActiveMatches} partidas simultâneas atingido. " +
                "Finalize uma partida antes de criar outra.");

        _repository.Add(match);

        await SyncPlayersFromGroupCoreAsync(groupId, match, ct);

        match.OpenAcceptation();

        await _repository.SaveChangesAsync(ct);

        await NotifyMatchInviteAsync(groupId, match.Id, ct);
        await _scheduler.ScheduleMatchRemindersAsync(match.Id, groupId, match.PlayedAt, ct);
        await _scheduler.ScheduleMatchNoQuorumReminderAsync(match.Id, groupId, match.PlayedAt, ct);

        return Result<MatchEntity>.Ok(match, "Partida criada com sucesso.", ResultStatus.Created);
    }

    public async Task<Result> GoToMatchMakingAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        match.GoToMatchMaking();

        await _context.SaveChangesAsync(ct);
        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result> GoToPostGameAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        match.GoToPostGame();

        await _context.SaveChangesAsync(ct);
        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result> UpdateAsync(Guid groupId, Guid matchId, UpdateMatchDto dto, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForSimpleUpdateAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        match.UpdateDetails(groupId, dto.PlayedAt, dto.PlaceName, matchIdFromRoute: matchId, dtoId: dto.Id);
        await SyncManualScheduleEntryAsync(groupId, matchId, match.PlayedAt, ct);

        await _context.SaveChangesAsync(ct);
        await _scheduler.RescheduleMatchRemindersAsync(matchId, groupId, match.PlayedAt, ct);
        await _scheduler.RescheduleMatchNoQuorumReminderAsync(matchId, groupId, match.PlayedAt, ct);
        return Result.Ok("Partida atualizada com sucesso.");
    }

    private async Task SyncManualScheduleEntryAsync(Guid groupId, Guid matchId, DateTime playedAt, CancellationToken ct)
    {
        var settings = await _context.GroupSettings
            .FirstOrDefaultAsync(x => x.GroupId == groupId, ct);

        if (settings is null)
            return;

        var entries = settings.GetManualMatchSchedules().ToList();
        var entry = entries.FirstOrDefault(x => x.MatchId == matchId);
        if (entry is null)
            return;

        entry.PlayedAt = DateTime.SpecifyKind(playedAt, DateTimeKind.Utc);
        settings.SetMatchScheduling(
            settings.MatchSchedulingEnabled,
            settings.MatchSchedulingMode,
            settings.MatchScheduleDayOfWeek,
            settings.MatchScheduleTime,
            entries);
    }

    public async Task<Result> DeleteAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForSimpleUpdateOrNullAsync(groupId, matchId, ct);
        if (match is null) return Result.Ok("Partida removida com sucesso.");

        match.EnsureCanDelete();

        await _scheduler.CancelMatchRemindersAsync(matchId, ct);
        await _scheduler.CancelMatchNoQuorumReminderAsync(matchId, ct);
        await _scheduler.CancelMvpAutoFinalizeAsync(matchId, ct);
        _repository.Remove(match);
        await _repository.SaveChangesAsync(ct);
        return Result.Ok("Partida removida com sucesso.");
    }

    public async Task<Result> SyncPlayersFromGroupAsync(Guid groupId, MatchEntity match, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        await SyncPlayersFromGroupCoreAsync(groupId, match, ct);

        await _context.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> SyncPlayersFromGroupAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForPlayersUpdateAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var existingPlayerIds = match.Players.Select(mp => mp.PlayerId).ToHashSet();

        await SyncPlayersFromGroupCoreAsync(groupId, match, ct);

        await _context.SaveChangesAsync(ct);

        _ = NotifyNewlyAddedToMatchAsync(groupId, matchId, existingPlayerIds, ct);

        return Result.Ok();
    }

    public async Task<Result> AcceptInviteAsync(Guid groupId, Guid matchId, Guid playerId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        match.AcceptInvite(playerId);

        await _context.SaveChangesAsync(ct);
        _ = NotifyAttendanceAsync(groupId, matchId, playerId, accepted: true, ct);
        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result> RejectInviteAsync(Guid groupId, Guid matchId, Guid playerId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        match.RejectInvite(playerId);

        await _context.SaveChangesAsync(ct);
        _ = NotifyAttendanceAsync(groupId, matchId, playerId, accepted: false, ct);
        return Result.Ok("Partida atualizada com sucesso.");
    }

    /// <summary>Aceita convite usando o userId do JWT — sem precisar do playerId.</summary>
    public async Task<Result> AcceptMyInviteAsync(Guid groupId, Guid matchId, Guid userId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var playerId = await _context.Players
            .Where(p => p.GroupId == groupId && p.UserId == userId && !p.IsGuest)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(ct);

        if (playerId == Guid.Empty)
            return Result.Fail("Jogador não encontrado no grupo.", ResultStatus.NotFound);

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        match.AcceptInvite(playerId);
        await _context.SaveChangesAsync(ct);
        _ = NotifyAttendanceAsync(groupId, matchId, playerId, accepted: true, ct);
        return Result.Ok("Presença confirmada.");
    }

    /// <summary>Rejeita convite usando o userId do JWT — sem precisar do playerId.</summary>
    public async Task<Result> RejectMyInviteAsync(Guid groupId, Guid matchId, Guid userId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var playerId = await _context.Players
            .Where(p => p.GroupId == groupId && p.UserId == userId && !p.IsGuest)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(ct);

        if (playerId == Guid.Empty)
            return Result.Fail("Jogador não encontrado no grupo.", ResultStatus.NotFound);

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        match.RejectInvite(playerId);
        await _context.SaveChangesAsync(ct);
        _ = NotifyAttendanceAsync(groupId, matchId, playerId, accepted: false, ct);
        return Result.Ok("Presença recusada.");
    }

    public async Task<Result> StartMatchAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForSimpleUpdateAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        match.Start();
        await _context.SaveChangesAsync(ct);

        await NotifyMatchStartedAsync(groupId, matchId, ct);

        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result> EndMatchAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForSimpleUpdateAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        match.End();
        await _context.SaveChangesAsync(ct);

        await NotifyMatchEndedAsync(groupId, matchId, ct);

        var autoFinalizeHours = await _context.GroupSettings
            .AsNoTracking()
            .Where(s => s.GroupId == groupId)
            .Select(s => s.AutoFinalizeMvpHours)
            .FirstOrDefaultAsync(ct);

        if (autoFinalizeHours.HasValue)
            await _scheduler.ScheduleMvpAutoFinalizeAsync(matchId, groupId, autoFinalizeHours.Value, ct);

        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result> VoteAsync(Guid groupId, Guid matchId, Guid voterMatchPlayerId, Guid votedMatchPlayerId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var vote = match.CreateVote(voterMatchPlayerId, votedMatchPlayerId);

        await _context.Votes.AddAsync(vote, ct);
        await _context.SaveChangesAsync(ct);

        // Se todos os jogadores não-convidados já votaram, persiste o MVP automaticamente
        var (tieRule, tieMax) = await LoadMvpTieRuleAsync(groupId, ct);
        if (match.AutoSetMvpIfAllVoted(tieRule, tieMax))
        {
            await _context.SaveChangesAsync(ct);
            _ = NotifyMvpDefinedAsync(groupId, matchId, ct);
        }

        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result<MatchPlayerEntity>> GetMvpAsync(Guid groupId, Guid matchId, CancellationToken ct = default)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<MatchPlayerEntity>.Fail(groupCheck.Error!, groupCheck.Status);

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        if (match is null)
            return Result<MatchPlayerEntity>.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var mvp = match.GetComputedMvp();
        if (mvp is null)
            return Result<MatchPlayerEntity>.Fail("MVP ainda não computado.", ResultStatus.NotFound);
        return Result<MatchPlayerEntity>.Ok(mvp);
    }

    public async Task<Result> SetScoreAsync(Guid groupId, Guid matchId, int teamAGoals, int teamBGoals, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForSimpleUpdateAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        match.SetScore(teamAGoals, teamBGoals);
        await _context.SaveChangesAsync(ct);
        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result> SetTeamColorsAsync(Guid groupId, Guid matchId, Guid? teamAColorId, Guid? teamBColorId, bool randomize, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForSimpleUpdateAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        if (randomize)
        {
            var colors = await _context.TeamColors.AsNoTracking().ToListAsync(ct);
            match.SetTeamColorsRandomly(colors);
            await _context.SaveChangesAsync(ct);
            return Result.Ok("Partida atualizada com sucesso.");
        }

        if (teamAColorId.HasValue)
        {
            var colorCheck = await EnsureTeamColorExistsAsync(teamAColorId.Value, "Cor do time A nao encontrada.", ct);
            if (!colorCheck.Success) return colorCheck;
        }

        if (teamBColorId.HasValue)
        {
            var colorCheck = await EnsureTeamColorExistsAsync(teamBColorId.Value, "Cor do time B nao encontrada.", ct);
            if (!colorCheck.Success) return colorCheck;
        }

        match.SetTeamColors(teamAColorId, teamBColorId);

        await _context.SaveChangesAsync(ct);
        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result> FinalizeMatchAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var (tieRule, tieMax) = await LoadMvpTieRuleAsync(groupId, ct);
        match.FinalizeByVotes(tieRule, tieMax);

        await _context.SaveChangesAsync(ct);

        // Resolve apostas imediatamente após finalização (evita resolução lazy com race condition)
        await _bets.ResolveMatchBetsAsync(matchId, ct);

        // Cancela jobs de auto-finalize agendados (caso a finalização seja manual)
        await _scheduler.CancelMvpAutoFinalizeAsync(matchId, ct);

        await NotifyMatchFinalizedAsync(groupId, matchId, ct);
        _ = NotifyMvpDefinedAsync(groupId, matchId, ct);

        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result> ReapplyMvpTieRuleAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        if (match.Status != Domain.Enums.MatchStatus.PostGame && match.Status != Domain.Enums.MatchStatus.Finalized)
            return Result.Fail("A partida precisa estar em PostGame ou Finalizada para recalcular o MVP.", ResultStatus.BadRequest);

        var (tieRule, tieMax) = await LoadMvpTieRuleAsync(groupId, ct);
        match.ReapplyMvpTieRule(tieRule, tieMax);
        await _context.SaveChangesAsync(ct);

        return Result.Ok("MVP recalculado com sucesso.");
    }

    public async Task<Result> AssignTeamsAsync(Guid groupId, Guid matchId, AssignTeamsDto dto, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var matchIdCheck = EnsureMatchId(matchId);
        if (!matchIdCheck.Success) return matchIdCheck;

        dto.TeamAMatchPlayerIds ??= [];
        dto.TeamBMatchPlayerIds ??= [];

        var match = await LoadMatchForPlayersUpdateAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var prevTeamA = match.Players.Where(p => p.Team == 1).Select(p => p.PlayerId).ToHashSet();
        var prevTeamB = match.Players.Where(p => p.Team == 2).Select(p => p.PlayerId).ToHashSet();
        var hadTeams  = prevTeamA.Count > 0 || prevTeamB.Count > 0;

        match.AssignTeams(dto.TeamAMatchPlayerIds, dto.TeamBMatchPlayerIds);

        var teamsChanged = hadTeams &&
            (!prevTeamA.SetEquals(dto.TeamAMatchPlayerIds) || !prevTeamB.SetEquals(dto.TeamBMatchPlayerIds));

        if (teamsChanged)
        {
            var bets = await _context.Set<MatchBetEntity>()
                .Include(b => b.Selections)
                .Where(b => b.MatchId == matchId && !b.IsResolved)
                .ToListAsync(ct);

            if (bets.Count > 0)
            {
                _context.Set<MatchBetSelectionEntity>().RemoveRange(bets.SelectMany(b => b.Selections));
                _context.Set<MatchBetEntity>().RemoveRange(bets);
            }
        }

        await _context.SaveChangesAsync(ct);

        await NotifyTeamsAssignedAsync(groupId, matchId, ct);

        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result> SwapPlayersByPlayerIdAsync(
        Guid groupId,
        Guid matchId,
        Guid playerAId,
        Guid playerBId,
        CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var matchIdCheck = EnsureMatchId(matchId);
        if (!matchIdCheck.Success) return matchIdCheck;

        if (playerAId == Guid.Empty || playerBId == Guid.Empty)
            return Result.Fail("PlayerId é obrigatório.");

        if (playerAId == playerBId)
            return Result.Fail("Não é possível trocar o mesmo jogador.");

        var match = await _context.Matches
            .Include(m => m.Players)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var mpA = match.Players.FirstOrDefault(p => p.PlayerId == playerAId);
        var mpB = match.Players.FirstOrDefault(p => p.PlayerId == playerBId);

        if (mpA is null || mpB is null)
            return Result.Fail("Um ou ambos os jogadores não pertencem a esta partida.");

        match.SwapPlayers(mpA.Id, mpB.Id);

        await _context.SaveChangesAsync(ct);
        _ = NotifyTeamsAssignedAsync(groupId, matchId, ct);
        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result> SetNoShowAsync(
        Guid groupId, Guid matchId, Guid matchPlayerId,
        bool didNotPlay, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForPlayersUpdateAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var player = match.Players.FirstOrDefault(p => p.Id == matchPlayerId);
        if (player is null)
            return Result.Fail("Jogador não encontrado na partida.", ResultStatus.NotFound);

        player.SetDidNotPlay(didNotPlay);
        await _context.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> SetPlayerRoleAsync(
        Guid groupId, Guid matchId, Guid matchPlayerId,
        SetPlayerRoleDto dto, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForPlayersUpdateAsync(groupId, matchId, ct);
        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        match.SetPlayerRole(matchPlayerId, dto.IsGoalkeeper);
        await _context.SaveChangesAsync(ct);
        return Result.Ok("Partida atualizada com sucesso.");
    }

    private async Task<MatchEntity?> LoadMatchForSimpleUpdateAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var check = EnsureMatchId(matchId);
        if (!check.Success) return null;

        return await _context.Matches
            .Include(m => m.Players)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);
    }

    private async Task<MatchEntity?> LoadMatchForSimpleUpdateOrNullAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var check = EnsureMatchId(matchId);
        if (!check.Success) return null;

        return await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);
    }

    private async Task<MatchEntity?> LoadMatchForDomainActionsAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var check = EnsureMatchId(matchId);
        if (!check.Success) return null;

        return await _context.Matches
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Include(m => m.Players).ThenInclude(mp => mp.Player)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<MatchEntity?> LoadMatchForPlayersUpdateAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var check = EnsureMatchId(matchId);
        if (!check.Success) return null;

        return await _context.Matches
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Include(m => m.Players)
            .FirstOrDefaultAsync(ct);
    }

    private async Task SyncPlayersFromGroupCoreAsync(Guid groupId, MatchEntity match, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(match);

        var players = await _context.Players
            .Where(p => p.GroupId == groupId && p.Status == Status.Active)
            .ToListAsync(ct);

        var matchDate = DateOnly.FromDateTime(match.PlayedAt);

        var userIds = players
            .Where(p => p.UserId != null)
            .Select(p => p.UserId!.Value)
            .Distinct()
            .ToList();

        var absenceByUser = userIds.Count > 0
            ? await _context.UserAbsences
                .Where(a => userIds.Contains(a.UserId) &&
                            a.StartDate <= matchDate &&
                            a.EndDate   >= matchDate)
                .ToDictionaryAsync(a => a.UserId, ct)
            : new Dictionary<Guid, UserAbsenceEntity>();

        foreach (var player in players)
        {
            var alreadyInMatch = match.Players.Any(mp => mp.PlayerId == player.Id);
            if (alreadyInMatch) continue;

            var mp = new MatchPlayerEntity(player.Id);
            match.AddPlayer(mp, player);

            // Registra explicitamente como Added: quando a partida já existe (Unchanged),
            // o DetectChanges marcaria o novo MatchPlayer (GUID já preenchido) como UPDATE,
            // causando DbUpdateConcurrencyException. Mesmo fix de SyncPlayerIntoActiveMatchesAsync.
            _context.MatchPlayers.Add(mp);

            if (player.UserId != null && absenceByUser.TryGetValue(player.UserId.Value, out var absence))
                mp.AutoRejectByAbsence(absence.Id);
        }
    }

    private async Task<Result> EnsureGroupExistsAsync(Guid groupId, CancellationToken ct)
    {
        var idCheck = EnsureGroupId(groupId);
        if (!idCheck.Success) return idCheck;

        var exists = await _context.Groups
            .AsNoTracking()
            .AnyAsync(g => g.Id == groupId, ct);

        if (!exists)
            return Result.Fail("Group não encontrado.", ResultStatus.NotFound);

        return Result.Ok();
    }

    private async Task<Result> EnsureTeamColorExistsAsync(Guid colorId, string errorMessage, CancellationToken ct)
    {
        var exists = await _context.TeamColors
            .AsNoTracking()
            .AnyAsync(c => c.Id == colorId, ct);

        if (!exists)
            return Result.Fail(errorMessage, ResultStatus.NotFound);

        return Result.Ok();
    }

    private static Result EnsureGroupId(Guid groupId)
    {
        if (groupId == Guid.Empty)
            return Result.Fail("GroupId é obrigatório.");

        return Result.Ok();
    }

    private static Result EnsureMatchId(Guid matchId)
    {
        if (matchId == Guid.Empty)
            return Result.Fail("MatchId é obrigatório.");

        return Result.Ok();
    }

    private async Task<(MvpTieRule rule, int maxPlayers)> LoadMvpTieRuleAsync(Guid groupId, CancellationToken ct)
    {
        var settings = await _context.GroupSettings
            .AsNoTracking()
            .Where(s => s.GroupId == groupId)
            .Select(s => new { s.MvpTieRule, s.MvpTieMaxPlayers })
            .FirstOrDefaultAsync(ct);

        return (settings?.MvpTieRule ?? MvpTieRule.AllMvp, settings?.MvpTieMaxPlayers ?? 2);
    }

    public async Task<Result> AddGoalAsync(Guid groupId, Guid matchId, AddGoalRequestDto dto, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var matchIdCheck = EnsureMatchId(matchId);
        if (!matchIdCheck.Success) return matchIdCheck;

        if (dto is null) return Result.Fail("Dto é obrigatório.");

        var match = await _context.Matches
            .Include(m => m.Players)
            .Include(m => m.Goals)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var scorerMp = match.Players.FirstOrDefault(p => p.PlayerId == dto.ScorerPlayerId);
        if (scorerMp is null)
            return Result.Fail("O jogador do gol nao pertence a esta partida.");

        MatchPlayerEntity? assistMp = null;
        if (dto.AssistPlayerId.HasValue)
        {
            assistMp = match.Players.FirstOrDefault(p => p.PlayerId == dto.AssistPlayerId.Value);
            if (assistMp is null)
                return Result.Fail("O jogador da assistencia nao pertence a esta partida.");
        }

        var seconds = MatchTimeParser.ParseToSeconds(dto.Time);

        match.AddGoalByMatchPlayer(
            scorerMatchPlayerId: scorerMp.Id,
            assistMatchPlayerId: assistMp?.Id,
            timeSeconds: seconds,
            isOwnGoal: dto.IsOwnGoal);

        await _context.SaveChangesAsync(ct);

        if (match.Status == Domain.Enums.MatchStatus.Finalized)
            await _bets.ReResolveMatchBetsAsync(matchId, ct);

        return Result.Ok("Gol adicionado com sucesso.");
    }

    public async Task<Result> UpdateGoalAsync(Guid groupId, Guid matchId, Guid goalId, UpdateGoalRequestDto dto, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var matchIdCheck = EnsureMatchId(matchId);
        if (!matchIdCheck.Success) return matchIdCheck;

        if (goalId == Guid.Empty)
            return Result.Fail("GoalId e obrigatorio.");

        if (dto is null) return Result.Fail("Dto é obrigatório.");

        var match = await _context.Matches
            .Include(m => m.Players)
            .Include(m => m.Goals)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var scorerMp = match.Players.FirstOrDefault(p => p.PlayerId == dto.ScorerPlayerId);
        if (scorerMp is null)
            return Result.Fail("O jogador do gol nao pertence a esta partida.");

        MatchPlayerEntity? assistMp = null;
        if (dto.AssistPlayerId.HasValue)
        {
            assistMp = match.Players.FirstOrDefault(p => p.PlayerId == dto.AssistPlayerId.Value);
            if (assistMp is null)
                return Result.Fail("O jogador da assistencia nao pertence a esta partida.");
        }

        var seconds = MatchTimeParser.ParseToSeconds(dto.Time);

        match.UpdateGoal(
            goalId: goalId,
            scorerMatchPlayerId: scorerMp.Id,
            assistMatchPlayerId: assistMp?.Id,
            timeSeconds: seconds,
            isOwnGoal: dto.IsOwnGoal);

        await _context.SaveChangesAsync(ct);

        if (match.Status == Domain.Enums.MatchStatus.Finalized)
            await _bets.ReResolveMatchBetsAsync(matchId, ct);

        return Result.Ok("Gol atualizado com sucesso.");
    }

    public async Task<Result> RemoveGoalAsync(Guid groupId, Guid matchId, Guid goalId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var matchIdCheck = EnsureMatchId(matchId);
        if (!matchIdCheck.Success) return matchIdCheck;

        if (goalId == Guid.Empty)
            return Result.Fail("GoalId e obrigatorio.");

        var match = await _context.Matches
            .Include(m => m.Goals)
            .Include(m => m.Players)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var goal = match.Goals.FirstOrDefault(g => g.Id == goalId);
        if (goal is null)
            return Result.Ok("Gol removido com sucesso.");

        match.RemoveGoal(goalId);

        _context.Goals.Remove(goal);

        await _context.SaveChangesAsync(ct);

        if (match.Status == Domain.Enums.MatchStatus.Finalized)
            await _bets.ReResolveMatchBetsAsync(matchId, ct);

        return Result.Ok("Gol removido com sucesso.");
    }

    public async Task<Result<MatchEntity>> GetCurrentAsync(Guid groupId, CancellationToken ct = default)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<MatchEntity>.Fail(groupCheck.Error!, groupCheck.Status);

        // "Current" = nearest upcoming non-finalized match (ascending PlayedAt).
        var match = await _context.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status != MatchStatus.Finalized)
            .OrderBy(m => m.PlayedAt)
            .FirstOrDefaultAsync(ct);

        return Result<MatchEntity>.Ok(match!);
    }

    public async Task<Result<List<MatchHeaderDto>>> GetUpcomingAsync(Guid groupId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<List<MatchHeaderDto>>.Fail(groupCheck.Error!, groupCheck.Status);

        var headers = await _context.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status != MatchStatus.Finalized)
            .OrderBy(m => m.PlayedAt)
            .Take(MatchConstants.MaxSimultaneousActiveMatches)
            .Select(m => new MatchHeaderDto
            {
                MatchId      = m.Id,
                GroupId      = m.GroupId,
                PlayedAt     = m.PlayedAt,
                PlaceName    = m.PlaceName,
                Status       = (short)m.Status,
                StatusName   = m.Status.ToString(),
                StepKey         = ToStepKey(m.Status),
                CanRewind       = m.Status > MatchStatus.Created,
                TeamAGoals      = m.TeamAGoals,
                TeamBGoals      = m.TeamBGoals,
                LinkedPollId    = m.LinkedPollId,
                ActualStartTime = m.ActualStartTime,
            })
            .ToListAsync(ct);

        return Result<List<MatchHeaderDto>>.Ok(headers);
    }

    public async Task<Result> SyncPlayerIntoActiveMatchesAsync(Guid groupId, Guid playerId, CancellationToken ct)
    {
        var player = await _context.Players
            .FirstOrDefaultAsync(p => p.Id == playerId && p.GroupId == groupId && p.Status == Status.Active, ct);

        if (player is null)
            return Result.Ok(); // Player not found, wrong group, or inactive — nothing to sync

        // Only sync into matches that are still in a pre-game phase (invite list is meaningful)
        var eligibleMatches = await _context.Matches
            .Where(m => m.GroupId == groupId &&
                        (m.Status == MatchStatus.Created || m.Status == MatchStatus.Acceptation))
            .Include(m => m.Players)
            .ToListAsync(ct);

        if (eligibleMatches.Count == 0)
            return Result.Ok();

        // Query the DB for matches where this player is already a participant.
        // Avoids relying on potentially stale in-memory navigation collections
        // (e.g. when the caller just added the player via _context.MatchPlayers.Add
        // without going through match.Players.Add, EF Core's fix-up may not update
        // the tracked List<T> in time).
        var eligibleMatchIds = eligibleMatches.Select(m => m.Id).ToList();
        var alreadyInMatchIds = (await _context.MatchPlayers
                .Where(mp => mp.PlayerId == playerId && eligibleMatchIds.Contains(mp.MatchId))
                .Select(mp => mp.MatchId)
                .ToListAsync(ct))
            .ToHashSet();

        // Pre-load absences for this player so we can auto-reject when they have a registered absence
        var absencesByDate = new Dictionary<DateOnly, UserAbsenceEntity>();
        if (player.UserId.HasValue)
        {
            var absences = await _context.UserAbsences
                .Where(a => a.UserId == player.UserId.Value)
                .ToListAsync(ct);

            foreach (var match in eligibleMatches)
            {
                var date    = DateOnly.FromDateTime(match.PlayedAt);
                var absence = absences.FirstOrDefault(a => a.StartDate <= date && a.EndDate >= date);
                if (absence is not null)
                    absencesByDate[date] = absence;
            }
        }

        foreach (var match in eligibleMatches)
        {
            if (alreadyInMatchIds.Contains(match.Id)) continue;

            var mp = new MatchPlayerEntity(player.Id);
            match.AddPlayer(mp, player);

            // AddPlayer adds mp to the in-memory collection only.
            // Explicitly register the entity as Added so EF Core generates INSERT,
            // not UPDATE (which it would do via DetectChanges snapshot-diff for
            // untracked entities with a non-empty GUID key).
            _context.MatchPlayers.Add(mp);

            var matchDate = DateOnly.FromDateTime(match.PlayedAt);
            if (player.UserId.HasValue && absencesByDate.TryGetValue(matchDate, out var absence))
                mp.AutoRejectByAbsence(absence.Id);
        }

        await _context.SaveChangesAsync(ct);
        return Result.Ok();
    }

    /// <summary>Maps a <see cref="MatchStatus"/> to the frontend wizard step-key string.</summary>
    private static string ToStepKey(MatchStatus status) => status switch
    {
        MatchStatus.Created     => MatchStepKeys.Create,
        MatchStatus.Acceptation => MatchStepKeys.Accept,
        MatchStatus.MatchMaking => MatchStepKeys.Teams,
        MatchStatus.Started     => MatchStepKeys.Playing,
        MatchStatus.Ended       => MatchStepKeys.Ended,
        MatchStatus.PostGame    => MatchStepKeys.Post,
        MatchStatus.Finalized   => MatchStepKeys.Done,
        _                       => MatchStepKeys.Create,
    };

    public async Task<Result> AddGoalsBulkAsync(Guid groupId, Guid matchId, AddGoalsBulkRequestDto dto, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var matchIdCheck = EnsureMatchId(matchId);
        if (!matchIdCheck.Success) return matchIdCheck;

        if (dto is null) return Result.Fail("Dto é obrigatório.");

        var goals = dto.Goals ?? new List<AddGoalRequestDto>();

        if (goals.Count == 0)
            return Result.Fail("A lista de gols nao pode ser vazia.");

        var match = await _context.Matches
            .Include(m => m.Players)
                .ThenInclude(mp => mp.Player)
            .Include(m => m.Goals)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var mpByPlayerId = match.Players.ToDictionary(p => p.PlayerId, p => p);

        await using var tx = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            foreach (var item in goals)
            {
                if (item.ScorerPlayerId == Guid.Empty)
                    throw new InvalidOperationException("ScorerPlayerId e obrigatorio.");

                if (!mpByPlayerId.TryGetValue(item.ScorerPlayerId, out var scorerMp))
                    throw new InvalidOperationException("O jogador do gol nao pertence a esta partida.");

                MatchPlayerEntity? assistMp = null;
                if (item.AssistPlayerId.HasValue)
                {
                    if (item.AssistPlayerId.Value == Guid.Empty)
                        throw new InvalidOperationException("AssistPlayerId invalido.");

                    if (!mpByPlayerId.TryGetValue(item.AssistPlayerId.Value, out assistMp))
                        throw new InvalidOperationException("O jogador da assistencia nao pertence a esta partida.");
                }

                var seconds = MatchTimeParser.ParseToSeconds(item.Time);

                match.AddGoalByMatchPlayer(
                    scorerMatchPlayerId: scorerMp.Id,
                    assistMatchPlayerId: assistMp?.Id,
                    timeSeconds: seconds);
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        return Result.Ok("Gols adicionados com sucesso.");
    }

    public async Task<Result> RewindOneStepAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var match = await _context.Matches
            .Where(m => m.Id == matchId && m.GroupId == groupId)
            .FirstOrDefaultAsync(ct);

        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var wasMatchMaking = match.Status == MatchStatus.MatchMaking;

        match.RewindOneStep();

        if (wasMatchMaking)
        {
            // Busca e deleta os MatchPlayers diretamente pelo DbSet (sem navegar pela coleção)
            var existingMps = await _context.MatchPlayers
                .Where(mp => mp.MatchId == matchId)
                .ToListAsync(ct);

            var savedResponses = existingMps
                .ToDictionary(mp => mp.PlayerId, mp => (mp.InviteResponse, mp.AutoRejectedByAbsenceId));

            _context.MatchPlayers.RemoveRange(existingMps);

            var groupPlayers = await _context.Players
                .AsNoTracking()
                .Where(p => p.GroupId == groupId && p.Status == Status.Active)
                .ToListAsync(ct);

            foreach (var player in groupPlayers)
            {
                var newMp = new MatchPlayerEntity(player.Id);
                newMp.AssignGroup(groupId);
                newMp.SetIsGoalkeeper(player.IsGoalkeeper);

                if (savedResponses.TryGetValue(player.Id, out var saved))
                {
                    newMp.RestoreInviteResponse(saved.InviteResponse);
                    if (saved.AutoRejectedByAbsenceId.HasValue)
                        newMp.AutoRejectByAbsence(saved.AutoRejectedByAbsenceId.Value);
                }

                _context.MatchPlayers.Add(newMp);
                // MatchId tem private set; usa Entry API para definir sem nav prop
                _context.Entry(newMp).Property(x => x.MatchId).CurrentValue = matchId;
            }
        }

        await _context.SaveChangesAsync(ct);
        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result<MatchHeaderDto>> GetHeaderAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<MatchHeaderDto>.Fail(groupCheck.Error!, groupCheck.Status);

        var matchIdCheck = EnsureMatchId(matchId);
        if (!matchIdCheck.Success) return Result<MatchHeaderDto>.Fail(matchIdCheck.Error!, matchIdCheck.Status);

        var dto = await _context.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Select(m => new MatchHeaderDto
            {
                MatchId      = m.Id,
                GroupId      = m.GroupId,
                PlayedAt     = m.PlayedAt,
                PlaceName    = m.PlaceName,
                Status       = (short)m.Status,
                StatusName   = m.Status.ToString(),
                StepKey         = ToStepKey(m.Status),
                CanRewind       = m.Status > MatchStatus.Created,
                TeamAGoals      = m.TeamAGoals,
                TeamBGoals      = m.TeamBGoals,
                LinkedPollId    = m.LinkedPollId,
                ActualStartTime = m.ActualStartTime,
            })
            .FirstOrDefaultAsync(ct);

        if (dto is null)
            return Result<MatchHeaderDto>.Fail("Partida não encontrada.", ResultStatus.NotFound);

        return Result<MatchHeaderDto>.Ok(dto);
    }

    public async Task<Result<MatchAcceptationDto>> GetAcceptationAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<MatchAcceptationDto>.Fail(groupCheck.Error!, groupCheck.Status);

        var matchIdCheck = EnsureMatchId(matchId);
        if (!matchIdCheck.Success) return Result<MatchAcceptationDto>.Fail(matchIdCheck.Error!, matchIdCheck.Status);

        var matchData = await _context.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Select(m => new
            {
                m.Id,
                Status = (short)m.Status,
                Players = m.Players
                    .Where(mp => mp.Player!.Status == Status.Active)
                    .OrderBy(p => p.Player!.Name)
                    .Select(mp => new PlayerInMatchDto
                    {
                        MatchPlayerId = mp.Id,
                        PlayerId = mp.PlayerId,
                        PlayerName = mp.Player!.Name,
                        IsGoalkeeper = mp.IsGoalkeeper,
                        IsGuest = mp.Player!.IsGuest,
                        Team = mp.Team,
                        InviteResponse = (short)mp.InviteResponse,
                        AbsenceType = mp.AutoRejectedByAbsenceId != null ? (int?)mp.AutoRejectedByAbsence!.AbsenceType : null,
                        AbsenceDescription = mp.AutoRejectedByAbsenceId != null
                            ? (mp.AutoRejectedByAbsence!.Description == null
                                ? AbsenceService.GetTypeName(mp.AutoRejectedByAbsence!.AbsenceType)
                                : AbsenceService.GetTypeName(mp.AutoRejectedByAbsence!.AbsenceType) + " - " + mp.AutoRejectedByAbsence!.Description)
                            : null,
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (matchData is null)
            return Result<MatchAcceptationDto>.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var settings = await _context.GroupSettings
            .AsNoTracking()
            .Where(s => s.GroupId == groupId)
            .Select(s => new { s.MaxPlayers, s.MinPlayers })
            .FirstOrDefaultAsync(ct);

        var maxPlayers = settings?.MaxPlayers ?? 0;
        var minPlayers = settings?.MinPlayers ?? 2;
        // Quando a partida já passou da aceitação, quem não respondeu não pode mais aceitar
        // → retorna como recusado no DTO (sem alterar o banco)
        var acceptationClosed = matchData.Status > (short)MatchStatus.Acceptation;

        // Quando a partida já passou da aceitação, quem não respondeu não pode mais aceitar
        // → altera o InviteResponse na instância DTO (sem tocar no banco)
        if (acceptationClosed)
        {
            foreach (var p in matchData.Players.Where(p => p.InviteResponse == (short)InviteResponse.None))
                p.InviteResponse = (short)InviteResponse.Rejected;
        }

        var accepted = matchData.Players.Where(p => p.InviteResponse == (short)InviteResponse.Accepted).ToList();
        var rejected = matchData.Players.Where(p => p.InviteResponse == (short)InviteResponse.Rejected).ToList();
        var pending = acceptationClosed
            ? []
            : matchData.Players.Where(p => p.InviteResponse == (short)InviteResponse.None).ToList();

        var overLimit = maxPlayers > 0 && accepted.Count > maxPlayers;
        var dto = new MatchAcceptationDto
        {
            MatchId = matchData.Id,
            Status = matchData.Status,
            MaxPlayers = maxPlayers,
            AcceptedOverLimit = overLimit,
            CanAdvanceToMatchmaking = !overLimit && accepted.Count >= Math.Max(2, minPlayers),
            AcceptedPlayers = accepted,
            RejectedPlayers = rejected,
            PendingPlayers = pending,
        };

        return Result<MatchAcceptationDto>.Ok(dto);
    }

    /// <summary>
    /// Versão resumida para o dashboard.
    /// </summary>
    public async Task<Result<MatchAcceptationDto>> GetAcceptationSummaryAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var result = await GetAcceptationAsync(groupId, matchId, ct);
        if (!result.Success) return result;

        var d = result.Data!;
        return Result<MatchAcceptationDto>.Ok(new MatchAcceptationDto
        {
            MatchId                  = d.MatchId,
            Status                   = d.Status,
            MaxPlayers               = d.MaxPlayers,
            AcceptedOverLimit        = d.AcceptedOverLimit,
            CanAdvanceToMatchmaking  = d.CanAdvanceToMatchmaking,
            AcceptedPlayers          = d.AcceptedPlayers,
            RejectedPlayers          = d.RejectedPlayers.Where(p => !p.IsGuest).ToList(),
            PendingPlayers           = d.PendingPlayers .Where(p => !p.IsGuest).ToList(),
        });
    }

    public async Task<Result<MatchMatchMakingDto>> GetMatchMakingAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<MatchMatchMakingDto>.Fail(groupCheck.Error!, groupCheck.Status);

        var matchIdCheck = EnsureMatchId(matchId);
        if (!matchIdCheck.Success) return Result<MatchMatchMakingDto>.Fail(matchIdCheck.Error!, matchIdCheck.Status);

        var dto = await _context.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Select(m => new MatchMatchMakingDto
            {
                MatchId = m.Id,
                Status = (short)m.Status,

                TeamAColor = m.TeamAColorId == null ? null : new TeamColorDto
                {
                    Id = m.TeamAColor!.Id,
                    Name = m.TeamAColor!.Name,
                    HexValue = m.TeamAColor!.HexValue
                },

                TeamBColor = m.TeamBColorId == null ? null : new TeamColorDto
                {
                    Id = m.TeamBColor!.Id,
                    Name = m.TeamBColor!.Name,
                    HexValue = m.TeamBColor!.HexValue
                },

                TeamAPlayers = m.Players
                    .Where(p => p.Team == 1 && p.Player!.Status == Status.Active)
                    .OrderByDescending(p => p.IsGoalkeeper)
                    .ThenBy(p => p.Player!.Name)
                    .Select(mp => new PlayerInMatchDto
                    {
                        MatchPlayerId = mp.Id,
                        PlayerId      = mp.PlayerId,
                        PlayerName    = mp.Player!.Name,
                        IsGoalkeeper  = mp.IsGoalkeeper,
                        IsGuest       = mp.Player!.IsGuest,
                        Team          = mp.Team,
                        InviteResponse = (short)mp.InviteResponse,
                    })
                    .ToList(),

                TeamBPlayers = m.Players
                    .Where(p => p.Team == 2 && p.Player!.Status == Status.Active)
                    .OrderByDescending(p => p.IsGoalkeeper)
                    .ThenBy(p => p.Player!.Name)
                    .Select(mp => new PlayerInMatchDto
                    {
                        MatchPlayerId = mp.Id,
                        PlayerId      = mp.PlayerId,
                        PlayerName    = mp.Player!.Name,
                        IsGoalkeeper  = mp.IsGoalkeeper,
                        IsGuest       = mp.Player!.IsGuest,
                        Team          = mp.Team,
                        InviteResponse = (short)mp.InviteResponse,
                    })
                    .ToList(),

                UnassignedPlayers = m.Players
                    .Where(p => p.Team == 0
                             && p.Player!.Status == Status.Active
                             && p.InviteResponse == InviteResponse.Accepted)
                    .OrderBy(p => p.Player!.Name)
                    .Select(mp => new PlayerInMatchDto
                    {
                        MatchPlayerId = mp.Id,
                        PlayerId      = mp.PlayerId,
                        PlayerName    = mp.Player!.Name,
                        IsGoalkeeper  = mp.IsGoalkeeper,
                        IsGuest       = mp.Player!.IsGuest,
                        Team          = mp.Team,
                        InviteResponse = (short)mp.InviteResponse,
                    })
                    .ToList(),

                ColorsLocked   = m.TeamAColorId != null || m.TeamBColorId != null,
                TeamsAssigned  = m.Players.Any(p => p.Team == 1) && m.Players.Any(p => p.Team == 2),
                CanStartMatch  = m.Players.Any(p => p.Team == 1) && m.Players.Any(p => p.Team == 2),

                Participants = m.Players
                    .Where(p => (p.Team == 1 || p.Team == 2) && p.Player!.Status == Status.Active)
                    .OrderByDescending(p => p.IsGoalkeeper)
                    .ThenBy(p => p.Player!.Name)
                    .Select(mp => new PlayerInMatchDto
                    {
                        MatchPlayerId = mp.Id,
                        PlayerId      = mp.PlayerId,
                        PlayerName    = mp.Player!.Name,
                        IsGoalkeeper  = mp.IsGoalkeeper,
                        IsGuest       = mp.Player!.IsGuest,
                        Team          = mp.Team,
                        InviteResponse = (short)mp.InviteResponse,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (dto is null)
            return Result<MatchMatchMakingDto>.Fail("Partida não encontrada.", ResultStatus.NotFound);

        return Result<MatchMatchMakingDto>.Ok(dto);
    }

    public async Task<Result<MatchPostGameDto>> GetPostGameAsync(Guid groupId, Guid matchId, CancellationToken ct, Guid? requestingUserId = null)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<MatchPostGameDto>.Fail(groupCheck.Error!, groupCheck.Status);

        var matchIdCheck = EnsureMatchId(matchId);
        if (!matchIdCheck.Success) return Result<MatchPostGameDto>.Fail(matchIdCheck.Error!, matchIdCheck.Status);

        var baseData = await _context.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Select(m => new
            {
                m.Id,
                Status = (short)m.Status,
                m.TeamAGoals,
                m.TeamBGoals,

                Players = m.Players.Select(p => new
                {
                    p.Id,
                    p.PlayerId,
                    PlayerName = p.Player!.Name,
                    p.Team,
                    p.IsGoalkeeper,
                    IsGuest = p.Player!.IsGuest,
                    p.IsMvp,
                    p.DidNotPlay,
                    PlayerUserId = p.Player!.UserId,
                }).ToList(),

                Votes = m.Votes.Select(v => new
                {
                    v.Id,
                    v.VoterId,
                    v.VotedForId
                }).ToList(),

                Goals = m.Goals.Select(g => new
                {
                    g.Id,
                    g.ScorerMatchPlayerId,
                    g.AssistMatchPlayerId,
                    g.TimeSeconds,
                    g.CreateDate,
                    g.IsOwnGoal
                }).ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (baseData is null)
            return Result<MatchPostGameDto>.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var nameByMpId = baseData.Players.ToDictionary(x => x.Id, x => x.PlayerName);
        var playerIdByMpId = baseData.Players.ToDictionary(x => x.Id, x => x.PlayerId);
        var teamByMpId = baseData.Players.ToDictionary(x => x.Id, x => x.Team);

        // Elegíveis = não-convidados que ainda não votaram
        var voterIds = new HashSet<Guid>(baseData.Votes.Select(v => v.VoterId));
        var eligibleVoters = baseData.Players
            .Where(p => !p.IsGuest && !voterIds.Contains(p.Id) && (p.Team == 1 || p.Team == 2))
            .Select(p => new PlayerInMatchDto
            {
                MatchPlayerId = p.Id,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                IsGoalkeeper = p.IsGoalkeeper,
                IsGuest = p.IsGuest,
                Team = p.Team
            })
            .OrderBy(p => p.PlayerName)
            .ToList();

        var allVoted = eligibleVoters.Count == 0 && baseData.Players.Any(p => !p.IsGuest && (p.Team == 1 || p.Team == 2));

        // Votos individuais com nomes
        var individualVotes = baseData.Votes
            .Select(v => new VoteDto
            {
                VoteId = v.Id,
                VoterMatchPlayerId = v.VoterId,
                VotedForMatchPlayerId = v.VotedForId,
                VoterName = nameByMpId.TryGetValue(v.VoterId, out var vn) ? vn : string.Empty,
                VotedForName = nameByMpId.TryGetValue(v.VotedForId, out var vfn) ? vfn : string.Empty
            })
            .ToList();

        // voteCounts
        var voteCounts = baseData.Votes
            .Where(v => v.VotedForId != Guid.Empty)
            .GroupBy(v => v.VotedForId)
            .Select(g => new VoteCountDto
            {
                VotedForMatchPlayerId = g.Key,
                VotedForName = nameByMpId.TryGetValue(g.Key, out var n) ? n : string.Empty,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.VotedForName)
            .ToList();

        // MVPs vêm do flag persistido (definido quando todos votaram — pode haver empate)
        var computedMvps = baseData.Players
            .Where(p => p.IsMvp == true)
            .Select(p => new MatchMvpDto
            {
                MatchPlayerId = p.Id,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                Team = p.Team
            })
            .ToList();

        var teamByMpId2 = baseData.Players.ToDictionary(x => x.Id, x => x.Team);
        var orderedGoals = baseData.Goals
            .OrderBy(g => g.TimeSeconds ?? int.MaxValue)
            .ThenBy(g => g.CreateDate)
            .ToList();

        int runningA = 0, runningB = 0;
        var goals = orderedGoals.Select(g =>
            {
                nameByMpId.TryGetValue(g.ScorerMatchPlayerId, out var scorerName);
                playerIdByMpId.TryGetValue(g.ScorerMatchPlayerId, out var scorerPid);

                string? assistName = null;
                Guid? assistPid = null;
                if (g.AssistMatchPlayerId.HasValue)
                {
                    nameByMpId.TryGetValue(g.AssistMatchPlayerId.Value, out assistName);
                    if (playerIdByMpId.TryGetValue(g.AssistMatchPlayerId.Value, out var ap))
                        assistPid = ap;
                }

                // Calcula para qual time o gol vai (gol contra inverte)
                teamByMpId2.TryGetValue(g.ScorerMatchPlayerId, out var scorerTeam);
                var scoringTeam = g.IsOwnGoal
                    ? (scorerTeam == 1 ? 2 : 1)
                    : scorerTeam;
                if (scoringTeam == 1) runningA++;
                else if (scoringTeam == 2) runningB++;

                return new GoalDto
                {
                    GoalId = g.Id,
                    ScorerMatchPlayerId = g.ScorerMatchPlayerId,
                    AssistMatchPlayerId = g.AssistMatchPlayerId,
                    ScorerPlayerId = scorerPid,
                    ScorerName = scorerName ?? string.Empty,
                    AssistPlayerId = assistPid,
                    AssistName = assistName,
                    TimeSeconds = g.TimeSeconds,
                    Time = MatchTimeParser.FormatFromSeconds(g.TimeSeconds),
                    IsOwnGoal = g.IsOwnGoal,
                    ScoreAAfter = runningA,
                    ScoreBAfter = runningB,
                };
            })
            .ToList();

        var participants = baseData.Players
            .Where(p => p.Team == 1 || p.Team == 2)
            .OrderByDescending(p => p.IsGoalkeeper)
            .ThenBy(p => p.PlayerName)
            .Select(p => new PlayerInMatchDto
            {
                MatchPlayerId = p.Id,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                IsGoalkeeper = p.IsGoalkeeper,
                IsGuest = p.IsGuest,
                Team = p.Team
            })
            .ToList();

        // ── CanVote / HasVoted para o usuário autenticado ────────────────────
        bool? canVote = null;
        bool? hasVoted = null;
        Guid? myVotedForMatchPlayerId = null;

        if (requestingUserId.HasValue)
        {
            var myMatchPlayer = baseData.Players
                .FirstOrDefault(p => p.PlayerUserId == requestingUserId.Value);

            if (myMatchPlayer is not null)
            {
                var iVoted = voterIds.Contains(myMatchPlayer.Id);
                hasVoted = iVoted;
                canVote  = !myMatchPlayer.IsGuest
                           && (myMatchPlayer.Team == 1 || myMatchPlayer.Team == 2)
                           && !myMatchPlayer.DidNotPlay
                           && !iVoted;

                if (iVoted)
                    myVotedForMatchPlayerId = baseData.Votes
                        .FirstOrDefault(v => v.VoterId == myMatchPlayer.Id)?.VotedForId;
            }
        }

        return Result<MatchPostGameDto>.Ok(new MatchPostGameDto
        {
            MatchId = baseData.Id,
            Status = baseData.Status,
            TeamAGoals = baseData.TeamAGoals,
            TeamBGoals = baseData.TeamBGoals,
            ComputedMvps = computedMvps,
            VoteCounts = voteCounts,
            Votes = individualVotes,
            Goals = goals,
            AllVoted = allVoted,
            EligibleVoters = eligibleVoters,
            Participants = participants,
            CanVote = canVote,
            HasVoted = hasVoted,
            MyVotedForMatchPlayerId = myVotedForMatchPlayerId,
        });
    }

    public async Task<Result<PagedResultDto<MatchHistoryItemDto>>> GetHistoryAsync(
        Guid groupId,
        int take,
        int skip,
        CancellationToken cancellationToken,
        Guid? playerId = null)
    {
        if (groupId == Guid.Empty)
            return Result<PagedResultDto<MatchHistoryItemDto>>.Fail("GroupId e obrigatorio.");

        take = take <= 0 ? 20 : Math.Min(take, 100);
        skip = Math.Max(0, skip);

        IQueryable<MatchEntity> query = _context.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.Finalized);

        if (playerId.HasValue)
        {
            var playerIdValue = playerId.Value;

            query = query.Where(m => m.Players.Any(p =>
                p.PlayerId == playerIdValue &&
                p.Team > 0));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(m => m.PlayedAt)
            .Skip(skip)
            .Take(take)
            .Select(m => new MatchHistoryItemDto(
                m.Id,
                m.PlayedAt,
                m.TeamAGoals ?? 0,
                m.TeamBGoals ?? 0,
                (int)m.Status,
                m.Status.ToString(),
                m.PlaceName,
                m.TeamAColor != null ? m.TeamAColor.HexValue : null,
                m.TeamBColor != null ? m.TeamBColor.HexValue : null,
                m.Players
                    .Select(p => p.PlayerId)
                    .ToList(),
                m.LinkedPollId,
                m.LinkedPoll != null ? m.LinkedPoll.Title : null,
                m.LinkedPoll != null ? m.LinkedPoll.Type : null
            ))
            .ToListAsync(cancellationToken);

        var page = take > 0 ? (skip / take) + 1 : 1;
        return Result<PagedResultDto<MatchHistoryItemDto>>.Ok(
            ToPage<MatchHistoryItemDto>(items, page, take, total));
    }

    public async Task<Result<IReadOnlyList<PlayerRecentMatchDto>>> GetPlayerRecentMatchesAsync(
        Guid groupId,
        Guid playerId,
        int take,
        CancellationToken ct)
    {
        take = take <= 0 ? 5 : Math.Min(take, 20);

        // Uma única query — traz tudo que o dashboard precisa
        var rows = await _context.Matches
            .AsNoTracking()
            .Where(m =>
                m.GroupId == groupId &&
                m.Status == MatchStatus.Finalized &&
                m.Players.Any(p => p.PlayerId == playerId && p.Team > 0 && !p.DidNotPlay))
            .OrderByDescending(m => m.PlayedAt)
            .Take(take)
            .Select(m => new
            {
                m.Id,
                m.PlayedAt,
                m.TeamAGoals,
                m.TeamBGoals,
                m.PlaceName,
                StatusName = m.Status.ToString(),

                TeamAColorHex = m.TeamAColor != null ? m.TeamAColor.HexValue : null,
                TeamAColorName = m.TeamAColor != null ? m.TeamAColor.Name : null,
                TeamBColorHex = m.TeamBColor != null ? m.TeamBColor.HexValue : null,
                TeamBColorName = m.TeamBColor != null ? m.TeamBColor.Name : null,

                // Time do jogador (1=A / 2=B)
                PlayerTeam = m.Players
                    .Where(p => p.PlayerId == playerId)
                    .Select(p => (int)p.Team)
                    .FirstOrDefault(),

                // MatchPlayer.Id do jogador nesta partida (usado para gols/assists/mvp)
                PlayerMatchPlayerId = m.Players
                    .Where(p => p.PlayerId == playerId)
                    .Select(p => (Guid?)p.Id)
                    .FirstOrDefault(),

                // Gols: conta goals cujo ScorerMatchPlayer pertence ao jogador
                PlayerGoals = m.Goals.Count(g =>
                    !g.IsOwnGoal &&
                    m.Players.Any(mp => mp.Id == g.ScorerMatchPlayerId && mp.PlayerId == playerId)),

                // Assistências: conta goals cujo AssistMatchPlayer pertence ao jogador
                PlayerAssists = m.Goals.Count(g =>
                    g.AssistMatchPlayerId.HasValue &&
                    m.Players.Any(mp => mp.Id == g.AssistMatchPlayerId.Value && mp.PlayerId == playerId)),

                // Gols contra: IsOwnGoal=true e scorer é o jogador
                PlayerOwnGoals = m.Goals.Count(g =>
                    g.IsOwnGoal &&
                    m.Players.Any(mp => mp.Id == g.ScorerMatchPlayerId && mp.PlayerId == playerId)),

                // Votos recebidos por cada MatchPlayer — MVP calculado em memória
                VotedForIds = m.Votes.Select(v => v.VotedForId).ToList(),
            })
            .ToListAsync(ct);

        var result = rows.Select(m =>
        {
            // MVP = MatchPlayer.Id com mais votos (em memória — evita subquery complexa no EF)
            var mvpMatchPlayerId = m.VotedForIds
                .GroupBy(id => id)
                .OrderByDescending(g => g.Count())
                .Select(g => (Guid?)g.Key)
                .FirstOrDefault();

            return new PlayerRecentMatchDto(
                MatchId: m.Id,
                PlayedAt: m.PlayedAt,
                TeamAGoals: m.TeamAGoals ?? 0,
                TeamBGoals: m.TeamBGoals ?? 0,
                StatusName: m.StatusName,
                PlaceName: m.PlaceName,
                TeamAColorHex: m.TeamAColorHex,
                TeamAColorName: m.TeamAColorName,
                TeamBColorHex: m.TeamBColorHex,
                TeamBColorName: m.TeamBColorName,
                PlayerTeam:     m.PlayerTeam,
                PlayerGoals:     m.PlayerGoals,
                PlayerAssists:   m.PlayerAssists,
                PlayerOwnGoals:  m.PlayerOwnGoals,
                IsPlayerMvp:     mvpMatchPlayerId.HasValue && mvpMatchPlayerId == m.PlayerMatchPlayerId
            );
        }).ToList();

        return Result<IReadOnlyList<PlayerRecentMatchDto>>.Ok(result);
    }

    public async Task<Result<IReadOnlyList<PlayerRecentMatchDto>>> GetPlayerHistoryAsync(
        Guid groupId,
        Guid playerId,
        int? year,
        CancellationToken ct)
    {
        var query = _context.Matches
            .AsNoTracking()
            .Where(m =>
                m.GroupId == groupId &&
                m.Status == MatchStatus.Finalized &&
                m.Players.Any(p => p.PlayerId == playerId && p.Team > 0 && !p.DidNotPlay));

        if (year.HasValue)
            query = query.Where(m => m.PlayedAt.Year == year.Value);

        var rows = await query
            .OrderByDescending(m => m.PlayedAt)
            .Take(500)
            .Select(m => new
            {
                m.Id,
                m.PlayedAt,
                m.TeamAGoals,
                m.TeamBGoals,
                m.PlaceName,
                StatusName = m.Status.ToString(),
                TeamAColorHex  = m.TeamAColor != null ? m.TeamAColor.HexValue : null,
                TeamAColorName = m.TeamAColor != null ? m.TeamAColor.Name : null,
                TeamBColorHex  = m.TeamBColor != null ? m.TeamBColor.HexValue : null,
                TeamBColorName = m.TeamBColor != null ? m.TeamBColor.Name : null,
                PlayerTeam = m.Players
                    .Where(p => p.PlayerId == playerId)
                    .Select(p => (int)p.Team)
                    .FirstOrDefault(),
                PlayerGoals = m.Goals.Count(g =>
                    !g.IsOwnGoal &&
                    m.Players.Any(mp => mp.Id == g.ScorerMatchPlayerId && mp.PlayerId == playerId)),
                PlayerAssists = m.Goals.Count(g =>
                    g.AssistMatchPlayerId.HasValue &&
                    m.Players.Any(mp => mp.Id == g.AssistMatchPlayerId.Value && mp.PlayerId == playerId)),
                PlayerOwnGoals = m.Goals.Count(g =>
                    g.IsOwnGoal &&
                    m.Players.Any(mp => mp.Id == g.ScorerMatchPlayerId && mp.PlayerId == playerId)),
                IsPlayerMvp = m.Players
                    .Where(p => p.PlayerId == playerId)
                    .Select(p => p.IsMvp ?? false)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var result = rows.Select(m => new PlayerRecentMatchDto(
            MatchId:        m.Id,
            PlayedAt:       m.PlayedAt,
            TeamAGoals:     m.TeamAGoals ?? 0,
            TeamBGoals:     m.TeamBGoals ?? 0,
            StatusName:     m.StatusName,
            PlaceName:      m.PlaceName,
            TeamAColorHex:  m.TeamAColorHex,
            TeamAColorName: m.TeamAColorName,
            TeamBColorHex:  m.TeamBColorHex,
            TeamBColorName: m.TeamBColorName,
            PlayerTeam:     m.PlayerTeam,
            PlayerGoals:    m.PlayerGoals,
            PlayerAssists:  m.PlayerAssists,
            PlayerOwnGoals: m.PlayerOwnGoals,
            IsPlayerMvp:    m.IsPlayerMvp
        )).ToList();

        return Result<IReadOnlyList<PlayerRecentMatchDto>>.Ok(result);
    }

    private static PlayerInMatchDto ToPlayerDto(MatchPlayerEntity mp) => new()
    {
        MatchPlayerId      = mp.Id,
        PlayerId           = mp.PlayerId,
        PlayerName         = mp.Player?.Name ?? string.Empty,
        IsGoalkeeper       = mp.IsGoalkeeper,
        IsGuest            = mp.Player?.IsGuest ?? false,
        Team               = mp.Team,
        InviteResponse     = (short)mp.InviteResponse,
        IsMvp              = mp.IsMvp ?? false,
        AbsenceType        = mp.AutoRejectedByAbsenceId != null ? (int?)mp.AutoRejectedByAbsence?.AbsenceType : null,
        AbsenceDescription = BuildAbsenceDescription(
            mp.AutoRejectedByAbsenceId != null ? (int?)mp.AutoRejectedByAbsence?.AbsenceType : null,
            mp.AutoRejectedByAbsence?.Description),
        DidNotPlay         = mp.DidNotPlay,
    };

    internal static string? BuildAbsenceDescription(int? absenceType, string? rawDescription)
    {
        if (absenceType is null) return null;
        var typeName = AbsenceService.GetTypeName((BratnavaFC.Domain.Enums.AbsenceType)absenceType);
        return string.IsNullOrWhiteSpace(rawDescription)
            ? typeName
            : $"{typeName} - {rawDescription}";
    }

    private static MatchDetailsDto MapToDetailsDto(MatchEntity match)
    {
        var computedMvps = match.GetComputedMvps()
            .Select(p => new MatchMvpDto
            {
                MatchPlayerId = p.Id,
                PlayerId = p.PlayerId,
                PlayerName = p.Player?.Name ?? string.Empty,
                Team = p.Team
            })
            .ToList();

        var mpNameById = match.Players.ToDictionary(
            p => p.Id,
            p => p.Player?.Name ?? string.Empty
        );

        var teamAPlayers = match.Players.Where(p => p.Team == 1).ToList();
        var teamBPlayers = match.Players.Where(p => p.Team == 2).ToList();
        var unassigned   = match.Players.Where(p => p.Team == 0 && p.InviteResponse == InviteResponse.Accepted).ToList();

        var mpById = match.Players.ToDictionary(p => p.Id);

        var goals = match.Goals
            .OrderBy(g => g.TimeSeconds ?? int.MaxValue)
            .ThenBy(g => g.CreateDate)
            .Select(g =>
            {
                mpById.TryGetValue(g.ScorerMatchPlayerId, out var scorerMp);
                MatchPlayerEntity? assistMp = null;

                if (g.AssistMatchPlayerId.HasValue)
                    mpById.TryGetValue(g.AssistMatchPlayerId.Value, out assistMp);

                return new GoalDto
                {
                    GoalId = g.Id,

                    ScorerMatchPlayerId = g.ScorerMatchPlayerId,
                    AssistMatchPlayerId = g.AssistMatchPlayerId,

                    ScorerPlayerId = scorerMp?.PlayerId ?? Guid.Empty,
                    ScorerName = scorerMp?.Player?.Name ?? string.Empty,

                    AssistPlayerId = assistMp?.PlayerId,
                    AssistName = assistMp?.Player?.Name,

                    TimeSeconds = g.TimeSeconds,
                    Time = MatchTimeParser.FormatFromSeconds(g.TimeSeconds),
                    IsOwnGoal = g.IsOwnGoal
                };
            })
            .ToList();

        var voteCounts = match.Votes
            .Where(v => v.VotedForId != Guid.Empty)
            .GroupBy(v => v.VotedForId)
            .Select(g => new VoteCountDto
            {
                VotedForMatchPlayerId = g.Key,
                VotedForName = mpNameById.TryGetValue(g.Key, out var n) ? n : string.Empty,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.VotedForName)
            .ToList();

        return new MatchDetailsDto
        {
            MatchId = match.Id,
            GroupName = match.Group?.Name ?? string.Empty,
            GroupId = match.GroupId,
            PlayedAt = match.PlayedAt,
            PlaceName = match.PlaceName,

            Status = (short)match.Status,
            StatusName = match.Status.ToString(),

            TeamAGoals = match.TeamAGoals,
            TeamBGoals = match.TeamBGoals,

            TeamAColor = match.TeamAColorId is null || match.TeamAColor is null
                ? null
                : new TeamColorDto
                {
                    Id = match.TeamAColor.Id,
                    Name = match.TeamAColor.Name,
                    HexValue = match.TeamAColor.HexValue
                },

            TeamBColor = match.TeamBColorId is null || match.TeamBColor is null
                ? null
                : new TeamColorDto
                {
                    Id = match.TeamBColor.Id,
                    Name = match.TeamBColor.Name,
                    HexValue = match.TeamBColor.HexValue
                },

            ComputedMvps = computedMvps,

            TeamAPlayers = teamAPlayers.Select(ToPlayerDto).OrderBy(x => x.PlayerName).ToList(),
            TeamBPlayers = teamBPlayers.Select(ToPlayerDto).OrderBy(x => x.PlayerName).ToList(),
            UnassignedPlayers = unassigned.Select(ToPlayerDto).OrderBy(x => x.PlayerName).ToList(),

            Votes = match.Votes.Select(v => new VoteDto
            {
                VoteId = v.Id,
                VoterMatchPlayerId = v.VoterId,
                VotedForMatchPlayerId = v.VotedForId,
                VoterName = mpNameById.TryGetValue(v.VoterId, out var voterName) ? voterName : string.Empty,
                VotedForName = mpNameById.TryGetValue(v.VotedForId, out var votedName) ? votedName : string.Empty
            }).ToList(),

            VoteCounts = voteCounts,
            Goals = goals,

            LinkedPollId = match.LinkedPollId,
        };
    }

    // ── Adicionar convidado direto na partida (status Acceptation) ─────────────

    public async Task<Result> AddGuestToMatchAsync(Guid groupId, Guid matchId, AddGuestToMatchDto dto, CancellationToken ct)
    {
        var match = await _context.Matches
            .Include(m => m.Players)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
            return Result.Fail("Partida não encontrada.", ResultStatus.NotFound);

        if (match.Status != MatchStatus.Acceptation)
            return Result.Fail("Só é possível adicionar convidado quando a partida está em Acceptation.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Fail("Nome do convidado é obrigatório.");

        // Valida o GuestStarRating antes de criar o jogador
        if (dto.GuestStarRating.HasValue && (dto.GuestStarRating.Value < 1 || dto.GuestStarRating.Value > 5))
            throw new ArgumentException("GuestStarRating deve ser entre 1 e 5.");

        // Cria o player como guest no grupo da partida
        var guest = new PlayerEntity(dto.Name.Trim(), null, match.GroupId, 0m, dto.IsGoalkeeper, true, Status.Active);
        if (dto.GuestStarRating.HasValue)
            guest.SetGuestStarRating(dto.GuestStarRating.Value);
        _context.Players.Add(guest);

        // Cria o MatchPlayer para a partida (convidado já é auto-aceito)
        var mp = new MatchPlayerEntity(guest.Id);
        mp.AssignToMatch(match);   // define MatchId, GroupId
        mp.AssignToPlayer(guest);  // define navigation Player
        mp.AcceptInvite();
        _context.MatchPlayers.Add(mp);

        await _context.SaveChangesAsync(ct);

        // Sync the new guest into all other pre-game matches of the group so they can participate
        // in upcoming matches as well (guests keep a history and may return)
        await SyncPlayerIntoActiveMatchesAsync(match.GroupId, guest.Id, ct);

        return Result.Ok("Convidado adicionado com sucesso.");
    }

    // ── Notificações ──────────────────────────────────────────────────────────

    private async Task NotifyNewlyAddedToMatchAsync(
        Guid groupId, Guid matchId, HashSet<Guid> previousPlayerIds, CancellationToken ct)
    {
        try
        {
            var newUserIds = await _context.MatchPlayers
                .AsNoTracking()
                .Where(mp => mp.MatchId == matchId && !previousPlayerIds.Contains(mp.PlayerId))
                .Join(_context.Players.Where(p => p.UserId != null && !p.IsGuest),
                      mp => mp.PlayerId, p => p.Id,
                      (mp, p) => p.UserId!.Value)
                .Distinct()
                .ToListAsync(ct);

            if (newUserIds.Count == 0) return;

            await _push.SendToUsersAsync(
                newUserIds,
                title: "Você foi adicionado a uma partida! ⚽",
                body:  "Você foi incluído em uma partida. Confirme sua presença!",
                data:  new Dictionary<string, string> { ["type"] = "match_invite", ["groupId"] = groupId.ToString(), ["matchId"] = matchId.ToString() },
                groupId: groupId);
        }
        catch { /* notificação não crítica */ }
    }

    private Task NotifyMatchInviteAsync(Guid groupId, Guid matchId, CancellationToken ct) =>
        _push.SendDataOnlyToGroupAsync(
            groupId,
            new Dictionary<string, string>
            {
                ["type"] = "match_invite",
                ["groupId"] = groupId.ToString(),
                ["matchId"] = matchId.ToString(),
                ["title"] = "Convite para partida",
                ["body"] = "Você foi convidado para uma partida. Confirme sua presença!",
            },
            ct);

    private async Task NotifyMatchStartedAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var userIds = await GetMatchParticipantUserIdsAsync(matchId, ct);
        await _push.SendToUsersAsync(
            userIds,
            title: "Partida iniciada!",
            body: "A partida do seu grupo começou. Boa sorte!",
            data: new Dictionary<string, string> { ["type"] = "match_started", ["groupId"] = groupId.ToString(), ["matchId"] = matchId.ToString() },
            groupId: groupId);
    }

    private async Task NotifyMatchEndedAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var userIds = await GetMatchParticipantUserIdsAsync(matchId, ct);
        await _push.SendToUsersAsync(
            userIds,
            title: "Partida encerrada!",
            body: "A partida acabou! Vote no MVP antes que a votação feche.",
            data: new Dictionary<string, string> { ["type"] = "match_ended", ["groupId"] = groupId.ToString(), ["matchId"] = matchId.ToString() },
            groupId: groupId);
    }

    private async Task NotifyMatchFinalizedAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var userIds = await GetMatchParticipantUserIdsAsync(matchId, ct);
        await _push.SendToUsersAsync(
            userIds,
            title: "Partida finalizada!",
            body: "Confira os resultados e o MVP da partida.",
            data: new Dictionary<string, string> { ["type"] = "match_finalized", ["groupId"] = groupId.ToString(), ["matchId"] = matchId.ToString() },
            groupId: groupId);
    }

    /// <summary>
    /// Notifica admins sobre confirmação/recusa de presença e verifica se o quorum mínimo foi atingido.
    /// Fire-and-forget — não bloqueia a resposta do endpoint.
    /// </summary>
    private async Task NotifyAttendanceAsync(Guid groupId, Guid matchId, Guid playerId, bool accepted, CancellationToken ct)
    {
        try
        {
            var playerName = await _context.Players
                .AsNoTracking()
                .Where(p => p.Id == playerId)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(ct);

            var confirmedCount = await _context.MatchPlayers
                .AsNoTracking()
                .CountAsync(mp => mp.MatchId == matchId && mp.InviteResponse == InviteResponse.Accepted, ct);

            if (accepted)
            {
                await _push.SendToGroupAdminsAsync(
                    groupId,
                    title: $"{playerName ?? "Jogador"} confirmou presença.",
                    body:  $"Agora são {confirmedCount} confirmados.",
                    data:  new Dictionary<string, string> { ["type"] = "attendance_confirmed", ["groupId"] = groupId.ToString(), ["matchId"] = matchId.ToString() },
                    ct);

                // Quorum: notifica apenas quando o limiar é atingido exatamente
                var minPlayers = await _context.GroupSettings
                    .AsNoTracking()
                    .Where(s => s.GroupId == groupId)
                    .Select(s => (int?)s.MinPlayers)
                    .FirstOrDefaultAsync(ct);

                if (minPlayers.HasValue && minPlayers.Value > 0 && confirmedCount == minPlayers.Value)
                {
                    await _push.SendToGroupAdminsAsync(
                        groupId,
                        title: "Quorum atingido!",
                        body:  $"Já são {confirmedCount} jogadores confirmados para a partida.",
                        data:  new Dictionary<string, string> { ["type"] = "quorum_reached", ["groupId"] = groupId.ToString(), ["matchId"] = matchId.ToString() },
                        ct);
                }
            }
            else
            {
                await _push.SendToGroupAdminsAsync(
                    groupId,
                    title: $"{playerName ?? "Jogador"} recusou presença.",
                    body:  $"Ficou com {confirmedCount} confirmados.",
                    data:  new Dictionary<string, string> { ["type"] = "attendance_rejected", ["groupId"] = groupId.ToString(), ["matchId"] = matchId.ToString() },
                    ct);
            }
        }
        catch { /* notificação não crítica */ }
    }

    /// <summary>
    /// Notifica participantes da partida quando o MVP é definido (via votação ou finalização manual).
    /// Fire-and-forget — não bloqueia a resposta do endpoint.
    /// </summary>
    private async Task NotifyMvpDefinedAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        try
        {
            var mvpName = await _context.MatchPlayers
                .AsNoTracking()
                .Where(mp => mp.MatchId == matchId && mp.IsMvp == true)
                .Select(mp => mp.Player!.Name)
                .FirstOrDefaultAsync(ct);

            if (mvpName is null) return;

            var userIds = await GetMatchParticipantUserIdsAsync(matchId, ct);
            await _push.SendToUsersAsync(
                userIds,
                title: "MVP da partida!",
                body:  $"{mvpName} foi eleito o MVP! Parabéns!",
                data:  new Dictionary<string, string> { ["type"] = "match_mvp", ["groupId"] = groupId.ToString(), ["matchId"] = matchId.ToString() },
                groupId: groupId);
        }
        catch { /* notificação não crítica */ }
    }

    private async Task NotifyTeamsAssignedAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var userIds = await GetMatchParticipantUserIdsAsync(matchId, ct);
        await _push.SendToUsersAsync(
            userIds,
            title: "Times definidos!",
            body: "Os times da partida foram sorteados. Confira o seu time!",
            data: new Dictionary<string, string> { ["type"] = "teams_assigned", ["groupId"] = groupId.ToString(), ["matchId"] = matchId.ToString() },
            groupId: groupId);
    }

    /// <summary>
    /// Retorna os UserId dos jogadores que aceitaram o convite da partida.
    /// Usado para restringir notificações pós-matchmaking apenas aos participantes.
    /// </summary>
    private async Task<List<Guid>> GetMatchParticipantUserIdsAsync(Guid matchId, CancellationToken ct) =>
        await _context.MatchPlayers
            .AsNoTracking()
            .Where(mp => mp.MatchId == matchId
                      && mp.InviteResponse == InviteResponse.Accepted
                      && mp.Player!.UserId != null)
            .Select(mp => mp.Player!.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);

    public async Task<Result<List<ReplayClipDto>>> GetReplaysAsync(Guid groupId, Guid matchId, Guid? userId, CancellationToken ct)
    {
        var clips = await _context.ReplayClips
            .AsNoTracking()
            .Where(r => r.GroupId == groupId && r.MatchId == matchId)
            .OrderBy(r => r.RecordedAt)
            .ToListAsync(ct);

        var clipIds = clips.Select(c => c.Id).ToList();

        var likeCounts = await _context.ReplayLikes
            .AsNoTracking()
            .Where(l => clipIds.Contains(l.ClipId))
            .GroupBy(l => l.ClipId)
            .Select(g => new { ClipId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ClipId, g => g.Count, ct);

        var myLikes     = new HashSet<Guid>();
        var myFavorites = new HashSet<Guid>();

        if (userId.HasValue)
        {
            myLikes = (await _context.ReplayLikes
                .AsNoTracking()
                .Where(l => clipIds.Contains(l.ClipId) && l.UserId == userId.Value)
                .Select(l => l.ClipId)
                .ToListAsync(ct)).ToHashSet();

            myFavorites = (await _context.ReplayFavorites
                .AsNoTracking()
                .Where(f => clipIds.Contains(f.ClipId) && f.UserId == userId.Value)
                .Select(f => f.ClipId)
                .ToListAsync(ct)).ToHashSet();
        }

        var dtos = clips.Select(c => new ReplayClipDto(
            c.Id,
            c.ObjectKey,
            _replayUrls.GeneratePresignedUrl(c.ObjectKey),
            c.EventType.ToString(),
            c.RecordedAt,
            LikeCount:       likeCounts.GetValueOrDefault(c.Id, 0),
            IsLikedByMe:     myLikes.Contains(c.Id),
            IsFavoritedByMe: myFavorites.Contains(c.Id)
        )).ToList();

        return Result<List<ReplayClipDto>>.Ok(dtos);
    }

    public async Task<(bool IsLiked, int LikeCount)> ToggleLikeAsync(Guid clipId, Guid userId, CancellationToken ct)
    {
        var existing = await _context.ReplayLikes
            .FirstOrDefaultAsync(l => l.ClipId == clipId && l.UserId == userId, ct);

        if (existing is not null)
            _context.ReplayLikes.Remove(existing);
        else
            _context.ReplayLikes.Add(new ReplayLikeEntity(clipId, userId));

        await _context.SaveChangesAsync(ct);

        var count = await _context.ReplayLikes.CountAsync(l => l.ClipId == clipId, ct);
        return (existing is null, count); // true = now liked
    }

    public async Task<bool> ToggleFavoriteAsync(Guid clipId, Guid userId, CancellationToken ct)
    {
        var existing = await _context.ReplayFavorites
            .FirstOrDefaultAsync(f => f.ClipId == clipId && f.UserId == userId, ct);

        if (existing is not null)
            _context.ReplayFavorites.Remove(existing);
        else
            _context.ReplayFavorites.Add(new ReplayFavoriteEntity(clipId, userId));

        await _context.SaveChangesAsync(ct);
        return existing is null; // true = now favorited
    }

    public async Task<Result<List<ClipLikerDto>>> GetClipLikersAsync(Guid clipId, CancellationToken ct)
    {
        // OrderBy must come before the projection so EF Core can translate it to SQL.
        var likers = await (
            from l in _context.ReplayLikes.AsNoTracking()
            join u in _context.Users.AsNoTracking() on l.UserId equals u.Id
            where l.ClipId == clipId
            orderby l.CreatedAt descending
            select new ClipLikerDto(u.Id, u.UserName, l.CreatedAt)
        ).ToListAsync(ct);

        return Result<List<ClipLikerDto>>.Ok(likers);
    }

    private static PagedResultDto<T> ToPage<T>(IReadOnlyList<T> items, int page, int pageSize, int total)
        => new() { Page = page, PageSize = pageSize, Total = total, Items = items };

    /// <summary>Enriquece os clips de uma página com contagem de likes e flags do usuário.</summary>
    private async Task<List<LikedReplayClipDto>> BuildReplayDtosAsync(
        List<ReplayClipEntity> clips, Guid? userId, CancellationToken ct)
    {
        if (clips.Count == 0) return [];

        var clipIds = clips.Select(c => c.Id).ToList();

        var likeCounts = await _context.ReplayLikes
            .AsNoTracking()
            .Where(l => clipIds.Contains(l.ClipId))
            .GroupBy(l => l.ClipId)
            .Select(g => new { ClipId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ClipId, g => g.Count, ct);

        var myLikes     = new HashSet<Guid>();
        var myFavorites = new HashSet<Guid>();

        if (userId.HasValue)
        {
            myLikes = (await _context.ReplayLikes
                .AsNoTracking()
                .Where(l => clipIds.Contains(l.ClipId) && l.UserId == userId.Value)
                .Select(l => l.ClipId)
                .ToListAsync(ct)).ToHashSet();

            myFavorites = (await _context.ReplayFavorites
                .AsNoTracking()
                .Where(f => clipIds.Contains(f.ClipId) && f.UserId == userId.Value)
                .Select(f => f.ClipId)
                .ToListAsync(ct)).ToHashSet();
        }

        return clips.Select(c => new LikedReplayClipDto(
            c.Id,
            c.MatchId,
            c.ObjectKey,
            _replayUrls.GeneratePresignedUrl(c.ObjectKey),
            c.EventType.ToString(),
            c.RecordedAt,
            likeCounts.GetValueOrDefault(c.Id, 0),
            myLikes.Contains(c.Id),
            myFavorites.Contains(c.Id)
        )).ToList();
    }

    public async Task<Result<PagedResultDto<LikedReplayClipDto>>> GetLikedReplaysAsync(
        Guid groupId, Guid? userId, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        var groupClipIds = await _context.ReplayClips
            .AsNoTracking()
            .Where(c => c.GroupId == groupId)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (groupClipIds.Count == 0)
            return Result<PagedResultDto<LikedReplayClipDto>>.Ok(ToPage<LikedReplayClipDto>([], page, pageSize, 0));

        // Ordenação por popularidade: precisa das contagens antes de paginar
        var likeCounts = await _context.ReplayLikes
            .AsNoTracking()
            .Where(l => groupClipIds.Contains(l.ClipId))
            .GroupBy(l => l.ClipId)
            .Select(g => new { ClipId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ClipId, g => g.Count, ct);

        var orderedIds = likeCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();

        var total   = orderedIds.Count;
        var pageIds = orderedIds.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        if (pageIds.Count == 0)
            return Result<PagedResultDto<LikedReplayClipDto>>.Ok(ToPage<LikedReplayClipDto>([], page, pageSize, total));

        var clips = await _context.ReplayClips
            .AsNoTracking()
            .Where(c => pageIds.Contains(c.Id))
            .ToListAsync(ct);

        // Restaura a ordem por popularidade perdida no Contains
        clips = clips.OrderBy(c => pageIds.IndexOf(c.Id)).ToList();

        var dtos = await BuildReplayDtosAsync(clips, userId, ct);
        return Result<PagedResultDto<LikedReplayClipDto>>.Ok(ToPage(dtos, page, pageSize, total));
    }

    public async Task<Result<PagedResultDto<LikedReplayClipDto>>> GetAllGroupReplaysAsync(
        Guid groupId, Guid userId, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        var query = _context.ReplayClips
            .AsNoTracking()
            .Where(r => r.GroupId == groupId);

        var total = await query.CountAsync(ct);

        var clips = await query
            .OrderByDescending(r => r.RecordedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = await BuildReplayDtosAsync(clips, userId, ct);
        return Result<PagedResultDto<LikedReplayClipDto>>.Ok(ToPage(dtos, page, pageSize, total));
    }

    public async Task<Result<PagedResultDto<LikedReplayClipDto>>> GetMyLikesAsync(
        Guid groupId, Guid userId, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        var likedClipIds = await _context.ReplayLikes
            .AsNoTracking()
            .Where(l => l.UserId == userId)
            .Select(l => l.ClipId)
            .ToListAsync(ct);

        var query = _context.ReplayClips
            .AsNoTracking()
            .Where(c => c.GroupId == groupId && likedClipIds.Contains(c.Id));

        var total = await query.CountAsync(ct);

        var clips = await query
            .OrderByDescending(c => c.RecordedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = await BuildReplayDtosAsync(clips, userId, ct);
        return Result<PagedResultDto<LikedReplayClipDto>>.Ok(ToPage(dtos, page, pageSize, total));
    }

    public async Task<Result<PagedResultDto<LikedReplayClipDto>>> GetMyFavoritesAsync(
        Guid groupId, Guid userId, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        var favClipIds = await _context.ReplayFavorites
            .AsNoTracking()
            .Where(f => f.UserId == userId)
            .Select(f => f.ClipId)
            .ToListAsync(ct);

        var query = _context.ReplayClips
            .AsNoTracking()
            .Where(c => c.GroupId == groupId && favClipIds.Contains(c.Id));

        var total = await query.CountAsync(ct);

        var clips = await query
            .OrderByDescending(c => c.RecordedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = await BuildReplayDtosAsync(clips, userId, ct);
        return Result<PagedResultDto<LikedReplayClipDto>>.Ok(ToPage(dtos, page, pageSize, total));
    }

    /// <summary>
    /// Vincula (pollId != null) ou desvincula (pollId == null) uma votação da partida,
    /// mantendo as referências bidirecionais consistentes.
    /// </summary>
    public async Task<Result<Guid?>> SetLinkedPollAsync(Guid groupId, Guid matchId, Guid? pollId, CancellationToken ct)
    {
        var match = await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
            return Result<Guid?>.Fail("Partida não encontrada.", ResultStatus.NotFound);

        // Limpa a referência reversa da votação anteriormente vinculada (se houver)
        if (match.LinkedPollId.HasValue && match.LinkedPollId != pollId)
        {
            var oldPoll = await _context.Polls.FirstOrDefaultAsync(p => p.Id == match.LinkedPollId.Value, ct);
            oldPoll?.SetLinkedMatch(null);
        }

        if (pollId.HasValue)
        {
            var newPoll = await _context.Polls
                .FirstOrDefaultAsync(p => p.Id == pollId.Value && p.GroupId == groupId, ct);
            if (newPoll is null)
                return Result<Guid?>.Fail("Votação não encontrada neste grupo.", ResultStatus.BadRequest);

            // Se a votação já estava vinculada a outra partida, limpa o FK daquela partida também
            if (newPoll.LinkedMatchId.HasValue && newPoll.LinkedMatchId != matchId)
            {
                var oldMatch = await _context.Matches.FirstOrDefaultAsync(m => m.Id == newPoll.LinkedMatchId.Value, ct);
                oldMatch?.SetLinkedPoll(null);
            }

            newPoll.SetLinkedMatch(matchId);
        }

        match.SetLinkedPoll(pollId);
        await _context.SaveChangesAsync(ct);

        return Result<Guid?>.Ok(pollId, pollId.HasValue ? "Votação vinculada." : "Vínculo removido.");
    }

    /// <summary>Busca um clip de replay do grupo (ou null). Usado pelos endpoints de download/stream.</summary>
    public Task<ReplayClipEntity?> GetReplayClipAsync(Guid groupId, Guid clipId, CancellationToken ct)
        => _context.ReplayClips
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clipId && c.GroupId == groupId, ct);

    public async Task DeleteReplayAsync(Guid groupId, Guid clipId, CancellationToken ct)
    {
        var clip = await _context.ReplayClips
            .FirstOrDefaultAsync(c => c.Id == clipId && c.GroupId == groupId, ct);

        if (clip is null)
            return;

        _context.ReplayLikes.RemoveRange(
            _context.ReplayLikes.Where(l => l.ClipId == clipId));

        _context.ReplayFavorites.RemoveRange(
            _context.ReplayFavorites.Where(f => f.ClipId == clipId));

        _context.ReplayClips.Remove(clip);
        await _context.SaveChangesAsync(ct);

        // Remove do R2 — best-effort (falha silenciosa, registro já foi removido do banco)
        try { await _replayUrls.DeleteObjectAsync(clip.ObjectKey, ct); }
        catch { /* ignorar falha de R2 */ }
    }

    public async Task<Result<ReplayClipDto>> UploadReplayAsync(
        Guid groupId, Guid matchId, Guid userId,
        Stream content, string contentType, string fileName,
        string eventType, CancellationToken ct)
    {
        // Valida que a partida pertence ao grupo e que o usuário é admin
        var match = await _context.Matches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
            return Result<ReplayClipDto>.Fail("Partida não encontrada.");

        if (!Enum.TryParse<MatchEventType>(eventType, ignoreCase: true, out var parsedEventType))
            return Result<ReplayClipDto>.Fail("Tipo de evento inválido. Use 'Gol' ou 'Jogada'.");

        var ext      = Path.GetExtension(fileName).ToLowerInvariant();
        var safeExt  = ext is ".mp4" or ".mov" or ".webm" or ".avi" ? ext : ".mp4";
        var subpasta = parsedEventType == MatchEventType.Gol ? "gols" : "jogadas";
        var objectKey = $"{groupId}/{matchId}/{subpasta}/{Guid.NewGuid()}{safeExt}";

        string etag;
        try
        {
            etag = await _replayUrls.UploadObjectAsync(objectKey, content, contentType, ct);
        }
        catch (Exception)
        {
            return Result<ReplayClipDto>.Fail("Falha ao enviar vídeo para o storage. Tente novamente.");
        }

        var clip = new ReplayClipEntity(
            groupId,
            matchId,
            _replayUrls.BucketName,
            objectKey,
            contentType,
            etag,
            DateTimeOffset.UtcNow,
            parsedEventType);

        _context.ReplayClips.Add(clip);
        await _context.SaveChangesAsync(ct);

        var dto = new ReplayClipDto(
            clip.Id,
            clip.ObjectKey,
            _replayUrls.GeneratePresignedUrl(clip.ObjectKey),
            clip.EventType.ToString(),
            clip.RecordedAt,
            LikeCount:       0,
            IsLikedByMe:     false,
            IsFavoritedByMe: false);

        return Result<ReplayClipDto>.Ok(dto);
    }
}
