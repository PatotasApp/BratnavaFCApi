using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
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

    public MatchService(
        AppDbContext context,
        IRepositoryBase<MatchEntity> repository,
        IPushService push)
    {
        _context = context;
        _repository = repository;
        _push = push;
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
                PlayerId      = p.PlayerId,
                PlayerName    = p.Player?.Name ?? string.Empty,
                Team          = p.Team
            })
            .ToList();

        var mpNameById = match.Players.ToDictionary(
            p => p.Id,
            p => p.Player?.Name ?? string.Empty
        );

        var teamAPlayers = match.Players.Where(p => p.Team == 1).ToList();
        var teamBPlayers = match.Players.Where(p => p.Team == 2).ToList();
        var unassigned = match.Players.Where(p => p.Team == 0).ToList();

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
            Goals = goals
        };

        return Result<MatchDetailsDto>.Ok(dto);
    }

    public async Task<Result<MatchEntity>> Create(Guid groupId, MatchEntity match, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<MatchEntity>.Fail(groupCheck.Error!, groupCheck.Status);

        var hasOpenMatch = await _context.Matches
            .AsNoTracking()
            .AnyAsync(m => m.GroupId == groupId && m.Status != MatchStatus.Finalized, ct);

        if (hasOpenMatch)
            return Result<MatchEntity>.Fail("Ja existe uma partida em andamento (não finalizada) para este grupo.");

        _repository.Add(match);

        await SyncPlayersFromGroupCoreAsync(groupId, match, ct);

        match.OpenAcceptation();

        await _repository.SaveChangesAsync(ct);

        await NotifyMatchInviteAsync(groupId, match.Id, ct);

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

        await _context.SaveChangesAsync(ct);
        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result> DeleteAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return groupCheck;

        var match = await LoadMatchForSimpleUpdateOrNullAsync(groupId, matchId, ct);
        if (match is null) return Result.Ok("Partida removida com sucesso.");

        match.EnsureCanDelete();

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

        await SyncPlayersFromGroupCoreAsync(groupId, match, ct);

        await _context.SaveChangesAsync(ct);
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
            await _context.SaveChangesAsync(ct);

        return Result.Ok("Partida atualizada com sucesso.");
    }

    public async Task<Result<MatchPlayerEntity>> GetMvpAsync(Guid groupId, Guid matchId, CancellationToken ct = default)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<MatchPlayerEntity>.Fail(groupCheck.Error!, groupCheck.Status);

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        if (match is null)
            return Result<MatchPlayerEntity>.Fail("Partida não encontrada.", ResultStatus.NotFound);

        return Result<MatchPlayerEntity>.Ok(match.GetComputedMvp()!);
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

        await NotifyMatchFinalizedAsync(groupId, matchId, ct);

        return Result.Ok("Partida atualizada com sucesso.");
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

        match.AssignTeams(dto.TeamAMatchPlayerIds, dto.TeamBMatchPlayerIds);

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
        return Result.Ok("Partida atualizada com sucesso.");
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

        foreach (var player in players)
        {
            var alreadyInMatch = match.Players.Any(mp => mp.PlayerId == player.Id);
            if (alreadyInMatch) continue;

            var mp = new MatchPlayerEntity(player.Id);
            match.AddPlayer(mp, player);
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
        return Result.Ok("Gol removido com sucesso.");
    }

    public async Task<Result<MatchEntity>> GetCurrentAsync(Guid groupId, CancellationToken ct = default)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<MatchEntity>.Fail(groupCheck.Error!, groupCheck.Status);

        var match = await _context.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status != MatchStatus.Finalized)
            .OrderByDescending(m => m.PlayedAt)
            .FirstOrDefaultAsync(ct);

        return Result<MatchEntity>.Ok(match!);
    }

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
                .ToDictionary(mp => mp.PlayerId, mp => mp.InviteResponse);

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

                if (savedResponses.TryGetValue(player.Id, out var previousResponse))
                    newMp.InviteResponse = previousResponse;

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
                MatchId = m.Id,
                GroupId = m.GroupId,
                PlayedAt = m.PlayedAt,
                PlaceName = m.PlaceName,
                Status = (short)m.Status,
                StatusName = m.Status.ToString(),
                StepKey = m.Status == MatchStatus.Created    ? "create"  :
                          m.Status == MatchStatus.Acceptation ? "accept"  :
                          m.Status == MatchStatus.MatchMaking  ? "teams"   :
                          m.Status == MatchStatus.Started      ? "playing" :
                          m.Status == MatchStatus.Ended        ? "ended"   :
                          m.Status == MatchStatus.PostGame     ? "post"    :
                          m.Status == MatchStatus.Finalized    ? "done"    : "create",
                CanRewind = m.Status > MatchStatus.Created,
                TeamAGoals = m.TeamAGoals,
                TeamBGoals = m.TeamBGoals
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
                        InviteResponse = (short)mp.InviteResponse
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (matchData is null)
            return Result<MatchAcceptationDto>.Fail("Partida não encontrada.", ResultStatus.NotFound);

        var settings = await _context.GroupSettings
            .AsNoTracking()
            .Where(s => s.GroupId == groupId)
            .Select(s => new { s.MaxPlayers })
            .FirstOrDefaultAsync(ct);

        var maxPlayers = settings?.MaxPlayers ?? 0;
        var accepted = matchData.Players.Where(p => p.InviteResponse == (short)InviteResponse.Accepted).ToList();
        var rejected = matchData.Players.Where(p => p.InviteResponse == (short)InviteResponse.Rejected).ToList();
        var pending  = matchData.Players.Where(p => p.InviteResponse == (short)InviteResponse.None).ToList();

        var dto = new MatchAcceptationDto
        {
            MatchId = matchData.Id,
            Status  = matchData.Status,
            MaxPlayers       = maxPlayers,
            AcceptedOverLimit = maxPlayers > 0 && accepted.Count > maxPlayers,
            AcceptedPlayers  = accepted,
            RejectedPlayers  = rejected,
            PendingPlayers   = pending,
        };

        return Result<MatchAcceptationDto>.Ok(dto);
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
                        PlayerId = mp.PlayerId,
                        PlayerName = mp.Player!.Name,
                        IsGoalkeeper = mp.IsGoalkeeper,
                        IsGuest = mp.Player!.IsGuest,
                        Team = mp.Team,
                        InviteResponse = (short)mp.InviteResponse
                    })
                    .ToList(),

                TeamBPlayers = m.Players
                    .Where(p => p.Team == 2 && p.Player!.Status == Status.Active)
                    .OrderByDescending(p => p.IsGoalkeeper)
                    .ThenBy(p => p.Player!.Name)
                    .Select(mp => new PlayerInMatchDto
                    {
                        MatchPlayerId = mp.Id,
                        PlayerId = mp.PlayerId,
                        PlayerName = mp.Player!.Name,
                        IsGoalkeeper = mp.IsGoalkeeper,
                        IsGuest = mp.Player!.IsGuest,
                        Team = mp.Team,
                        InviteResponse = (short)mp.InviteResponse
                    })
                    .ToList(),

                UnassignedPlayers = m.Players
                    .Where(p => p.Team == 0 && p.Player!.Status == Status.Active)
                    .OrderBy(p => p.Player!.Name)
                    .Select(mp => new PlayerInMatchDto
                    {
                        MatchPlayerId = mp.Id,
                        PlayerId = mp.PlayerId,
                        PlayerName = mp.Player!.Name,
                        IsGoalkeeper = mp.IsGoalkeeper,
                        IsGuest = mp.Player!.IsGuest,
                        Team = mp.Team,
                        InviteResponse = (short)mp.InviteResponse
                    })
                    .ToList(),

                ColorsLocked = m.TeamAColorId != null || m.TeamBColorId != null,

                Participants = m.Players
                    .Where(p => (p.Team == 1 || p.Team == 2) && p.Player!.Status == Status.Active)
                    .OrderByDescending(p => p.IsGoalkeeper)
                    .ThenBy(p => p.Player!.Name)
                    .Select(mp => new PlayerInMatchDto
                    {
                        MatchPlayerId = mp.Id,
                        PlayerId = mp.PlayerId,
                        PlayerName = mp.Player!.Name,
                        IsGoalkeeper = mp.IsGoalkeeper,
                        IsGuest = mp.Player!.IsGuest,
                        Team = mp.Team,
                        InviteResponse = (short)mp.InviteResponse
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (dto is null)
            return Result<MatchMatchMakingDto>.Fail("Partida não encontrada.", ResultStatus.NotFound);

        return Result<MatchMatchMakingDto>.Ok(dto);
    }

    public async Task<Result<MatchPostGameDto>> GetPostGameAsync(Guid groupId, Guid matchId, CancellationToken ct)
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
                    p.IsMvp
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

        var nameByMpId     = baseData.Players.ToDictionary(x => x.Id, x => x.PlayerName);
        var playerIdByMpId = baseData.Players.ToDictionary(x => x.Id, x => x.PlayerId);
        var teamByMpId     = baseData.Players.ToDictionary(x => x.Id, x => x.Team);

        // Elegíveis = não-convidados que ainda não votaram
        var voterIds = new HashSet<Guid>(baseData.Votes.Select(v => v.VoterId));
        var eligibleVoters = baseData.Players
            .Where(p => !p.IsGuest && !voterIds.Contains(p.Id) && (p.Team == 1 || p.Team == 2))
            .Select(p => new PlayerInMatchDto
            {
                MatchPlayerId = p.Id,
                PlayerId      = p.PlayerId,
                PlayerName    = p.PlayerName,
                IsGoalkeeper  = p.IsGoalkeeper,
                IsGuest       = p.IsGuest,
                Team          = p.Team
            })
            .OrderBy(p => p.PlayerName)
            .ToList();

        var allVoted = eligibleVoters.Count == 0 && baseData.Players.Any(p => !p.IsGuest && (p.Team == 1 || p.Team == 2));

        // Votos individuais com nomes
        var individualVotes = baseData.Votes
            .Select(v => new VoteDto
            {
                VoteId                 = v.Id,
                VoterMatchPlayerId     = v.VoterId,
                VotedForMatchPlayerId  = v.VotedForId,
                VoterName              = nameByMpId.TryGetValue(v.VoterId,     out var vn)  ? vn  : string.Empty,
                VotedForName           = nameByMpId.TryGetValue(v.VotedForId,  out var vfn) ? vfn : string.Empty
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
                PlayerId      = p.PlayerId,
                PlayerName    = p.PlayerName,
                Team          = p.Team
            })
            .ToList();

        var goals = baseData.Goals
            .OrderBy(g => g.TimeSeconds ?? int.MaxValue)
            .ThenBy(g => g.CreateDate)
            .Select(g =>
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
                    IsOwnGoal = g.IsOwnGoal
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
                PlayerId      = p.PlayerId,
                PlayerName    = p.PlayerName,
                IsGoalkeeper  = p.IsGoalkeeper,
                IsGuest       = p.IsGuest,
                Team          = p.Team
            })
            .ToList();

        return Result<MatchPostGameDto>.Ok(new MatchPostGameDto
        {
            MatchId        = baseData.Id,
            Status         = baseData.Status,
            TeamAGoals     = baseData.TeamAGoals,
            TeamBGoals     = baseData.TeamBGoals,
            ComputedMvps   = computedMvps,
            VoteCounts     = voteCounts,
            Votes          = individualVotes,
            Goals          = goals,
            AllVoted       = allVoted,
            EligibleVoters = eligibleVoters,
            Participants   = participants
        });
    }

    public async Task<Result<IReadOnlyList<MatchHistoryItemDto>>> GetHistoryAsync(
        Guid groupId,
        int take,
        CancellationToken cancellationToken,
        Guid? playerId = null)
    {
        if (groupId == Guid.Empty)
            return Result<IReadOnlyList<MatchHistoryItemDto>>.Fail("GroupId e obrigatorio.");

        take = take <= 0 ? 200 : Math.Min(take, 500);

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

        var items = await query
            .OrderByDescending(m => m.PlayedAt)
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
                    .ToList()
            ))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<MatchHistoryItemDto>>.Ok(items);
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
                m.Players.Any(p => p.PlayerId == playerId && p.Team > 0))
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

                TeamAColorHex  = m.TeamAColor != null ? m.TeamAColor.HexValue : null,
                TeamAColorName = m.TeamAColor != null ? m.TeamAColor.Name     : null,
                TeamBColorHex  = m.TeamBColor != null ? m.TeamBColor.HexValue : null,
                TeamBColorName = m.TeamBColor != null ? m.TeamBColor.Name     : null,

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
                IsPlayerMvp:    mvpMatchPlayerId.HasValue && mvpMatchPlayerId == m.PlayerMatchPlayerId
            );
        }).ToList();

        return Result<IReadOnlyList<PlayerRecentMatchDto>>.Ok(result);
    }

    private static PlayerInMatchDto ToPlayerDto(MatchPlayerEntity mp) => new()
    {
        MatchPlayerId = mp.Id,
        PlayerId = mp.PlayerId,
        PlayerName = mp.Player?.Name ?? string.Empty,
        IsGoalkeeper = mp.IsGoalkeeper,
        IsGuest = mp.Player?.IsGuest ?? false,
        Team = mp.Team,
        InviteResponse = (short)mp.InviteResponse,
        IsMvp = mp.IsMvp ?? false
    };

    private static MatchDetailsDto MapToDetailsDto(MatchEntity match)
    {
        var computedMvps = match.GetComputedMvps()
            .Select(p => new MatchMvpDto
            {
                MatchPlayerId = p.Id,
                PlayerId      = p.PlayerId,
                PlayerName    = p.Player?.Name ?? string.Empty,
                Team          = p.Team
            })
            .ToList();

        var mpNameById = match.Players.ToDictionary(
            p => p.Id,
            p => p.Player?.Name ?? string.Empty
        );

        var teamAPlayers = match.Players.Where(p => p.Team == 1).ToList();
        var teamBPlayers = match.Players.Where(p => p.Team == 2).ToList();
        var unassigned = match.Players.Where(p => p.Team == 0).ToList();

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
            Goals = goals
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

        // Cria o MatchPlayer para a partida
        var mp = new MatchPlayerEntity(guest.Id);
        mp.AssignToMatch(match);   // define MatchId, GroupId
        mp.AssignToPlayer(guest);  // define navigation Player
        // Team = 0 já configurado no construtor de MatchPlayerEntity
        _context.MatchPlayers.Add(mp);

        await _context.SaveChangesAsync(ct);
        return Result.Ok("Convidado adicionado com sucesso.");
    }

    // ── Notificações ──────────────────────────────────────────────────────────

    private Task NotifyMatchInviteAsync(Guid groupId, Guid matchId, CancellationToken ct) =>
        _push.SendDataOnlyToGroupAsync(
            groupId,
            new Dictionary<string, string>
            {
                ["type"]    = "match_invite",
                ["groupId"] = groupId.ToString(),
                ["matchId"] = matchId.ToString(),
                ["title"]   = "Convite para partida",
                ["body"]    = "Você foi convidado para uma partida. Confirme sua presença!",
            },
            ct);

    private Task NotifyMatchStartedAsync(Guid groupId, Guid matchId, CancellationToken ct) =>
        _push.SendToGroupAsync(
            groupId,
            title: "Partida iniciada!",
            body:  "A partida do seu grupo começou. Boa sorte!",
            data:  new Dictionary<string, string> { ["type"] = "match_started", ["groupId"] = groupId.ToString(), ["matchId"] = matchId.ToString() },
            ct);

    private Task NotifyMatchEndedAsync(Guid groupId, Guid matchId, CancellationToken ct) =>
        _push.SendToGroupAsync(
            groupId,
            title: "Partida encerrada!",
            body:  "A partida acabou! Vote no MVP antes que a votação feche.",
            data:  new Dictionary<string, string> { ["type"] = "match_ended", ["groupId"] = groupId.ToString(), ["matchId"] = matchId.ToString() },
            ct);

    private Task NotifyMatchFinalizedAsync(Guid groupId, Guid matchId, CancellationToken ct) =>
        _push.SendToGroupAsync(
            groupId,
            title: "Partida finalizada!",
            body:  "Confira os resultados e o MVP da partida.",
            data:  new Dictionary<string, string> { ["type"] = "match_finalized", ["groupId"] = groupId.ToString(), ["matchId"] = matchId.ToString() },
            ct);

    private Task NotifyTeamsAssignedAsync(Guid groupId, Guid matchId, CancellationToken ct) =>
        _push.SendToGroupAsync(
            groupId,
            title: "Times definidos!",
            body:  "Os times da partida foram sorteados. Confira o seu time!",
            data:  new Dictionary<string, string> { ["type"] = "teams_assigned", ["groupId"] = groupId.ToString(), ["matchId"] = matchId.ToString() },
            ct);
}
