using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class MatchService : IMatchService
{
    private readonly AppDbContext _context;
    private readonly IRepositoryBase<MatchEntity> _repository;

    public MatchService(AppDbContext context, IRepositoryBase<MatchEntity> repository)
    {
        _context = context;
        _repository = repository;
    }

    public async Task<IEnumerable<MatchEntity>> GetAllAsync(Guid groupId, CancellationToken ct = default)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        return await _context.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .Include(m => m.Players).ThenInclude(mp => mp.Player)
            .Include(m => m.Votes)
            .ToListAsync(ct);
    }

    public async Task<MatchEntity?> GetByIdAsync(Guid groupId, Guid matchId, CancellationToken ct = default)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        return await _context.Matches
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Include(m => m.Players).ThenInclude(mp => mp.Player)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<MatchDetailsDto?> GetDetailsAsync(Guid matchId, CancellationToken ct)
    {
        var match = await _context.Matches
            .AsNoTracking()
            .Include(m => m.Group)
            .Include(m => m.TeamAColor)
            .Include(m => m.TeamBColor)
            .Include(m => m.Players).ThenInclude(mp => mp.Player)
            .Include(m => m.Votes).ThenInclude(v => v.Voter)
            .Include(m => m.Votes).ThenInclude(v => v.VotedFor)
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);

        if (match is null) return null;

        var computedMvp = match.GetComputedMvp();

        var mpNameById = match.Players.ToDictionary(
            p => p.Id,
            p => p.Player?.Name ?? string.Empty
        );

        var teamAPlayers = match.Players.Where(p => p.Team == 1).ToList();
        var teamBPlayers = match.Players.Where(p => p.Team == 2).ToList();
        var unassigned = match.Players.Where(p => p.Team == 0).ToList();

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

            ComputedMvp = computedMvp is null
                ? null
                : new MatchMvpDto
                {
                    MatchPlayerId = computedMvp.Id,
                    PlayerId = computedMvp.PlayerId,
                    PlayerName = computedMvp.Player?.Name ?? string.Empty,
                    Team = computedMvp.Team
                },

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
        };
    }

    public async Task<MatchEntity> Create(Guid groupId, MatchEntity match, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        _repository.Add(match);

        await SyncPlayersFromGroupCoreAsync(groupId, match, ct);

        await _repository.SaveChangesAsync(ct);

        return match;
    }

    public async Task UpdateAsync(Guid groupId, Guid matchId, UpdateMatchDto dto, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var match = await LoadMatchForSimpleUpdateAsync(groupId, matchId, ct);

        match.UpdateDetails(groupId, dto.PlayedAt, dto.PlaceName, matchIdFromRoute: matchId, dtoId: dto.Id);

        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var match = await LoadMatchForSimpleUpdateOrNullAsync(groupId, matchId, ct);
        if (match is null) return;

        match.EnsureCanDelete();

        _repository.Remove(match);
        await _repository.SaveChangesAsync(ct);
    }

    public async Task SyncPlayersFromGroupAsync(Guid groupId, MatchEntity match, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        await SyncPlayersFromGroupCoreAsync(groupId, match, ct);

        await _context.SaveChangesAsync(ct);
    }

    public async Task SyncPlayersFromGroupAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var match = await LoadMatchForPlayersUpdateAsync(groupId, matchId, ct);

        await SyncPlayersFromGroupCoreAsync(groupId, match, ct);

        await _context.SaveChangesAsync(ct);
    }

    public async Task AcceptInviteAsync(Guid groupId, Guid matchId, Guid playerId, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        match.AcceptInvite(playerId);

        await _context.SaveChangesAsync(ct);
    }

    public async Task RejectInviteAsync(Guid groupId, Guid matchId, Guid playerId, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        match.RejectInvite(playerId);

        await _context.SaveChangesAsync(ct);
    }

    public async Task StartMatchAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var match = await LoadMatchForSimpleUpdateAsync(groupId, matchId, ct);

        match.Start();
        await _context.SaveChangesAsync(ct);
    }

    public async Task EndMatchAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var match = await LoadMatchForSimpleUpdateAsync(groupId, matchId, ct);

        match.End();
        await _context.SaveChangesAsync(ct);
    }

    public async Task VoteAsync(Guid groupId, Guid matchId, Guid voterMatchPlayerId, Guid votedMatchPlayerId, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        var vote = match.CreateVote(voterMatchPlayerId, votedMatchPlayerId);

        await _context.Votes.AddAsync(vote, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<MatchPlayerEntity?> GetMvpAsync(Guid groupId, Guid matchId, CancellationToken ct = default)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        return match.GetComputedMvp();
    }

    public async Task SetScoreAsync(Guid groupId, Guid matchId, int teamAGoals, int teamBGoals, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var match = await LoadMatchForSimpleUpdateAsync(groupId, matchId, ct);

        match.SetScore(teamAGoals, teamBGoals);
        await _context.SaveChangesAsync(ct);
    }

    public async Task SetTeamColorsAsync(Guid groupId, Guid matchId, Guid? teamAColorId, Guid? teamBColorId, bool randomize, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var match = await LoadMatchForSimpleUpdateAsync(groupId, matchId, ct);

        if (randomize)
        {
            var colors = await _context.TeamColors.AsNoTracking().ToListAsync(ct);
            match.SetTeamColorsRandomly(colors);
            await _context.SaveChangesAsync(ct);
            return;
        }

        if (teamAColorId.HasValue)
            await EnsureTeamColorExistsAsync(teamAColorId.Value, "Cor do time A nao encontrada.", ct);

        if (teamBColorId.HasValue)
            await EnsureTeamColorExistsAsync(teamBColorId.Value, "Cor do time B nao encontrada.", ct);

        match.SetTeamColors(teamAColorId, teamBColorId);

        await _context.SaveChangesAsync(ct);
    }

    public async Task FinalizeMatchAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);

        var match = await LoadMatchForDomainActionsAsync(groupId, matchId, ct);
        match.FinalizeByVotes();

        await _context.SaveChangesAsync(ct);
    }

    public async Task AssignTeamsAsync(Guid groupId, Guid matchId, AssignTeamsDto dto, CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);
        EnsureMatchId(matchId);

        dto.TeamAMatchPlayerIds ??= [];
        dto.TeamBMatchPlayerIds ??= [];

        var match = await LoadMatchForPlayersUpdateAsync(groupId, matchId, ct);

        match.AssignTeams(dto.TeamAMatchPlayerIds, dto.TeamBMatchPlayerIds);

        await _context.SaveChangesAsync(ct);
    }

    public async Task SwapPlayersByPlayerIdAsync(
        Guid groupId,
        Guid matchId,
        Guid playerAId,
        Guid playerBId,
        CancellationToken ct)
    {
        await EnsureGroupExistsAsync(groupId, ct);
        EnsureMatchId(matchId);

        if (playerAId == Guid.Empty || playerBId == Guid.Empty)
            throw new InvalidOperationException("PlayerId a© obrigata³rio.");

        if (playerAId == playerBId)
            throw new InvalidOperationException("Na£o a© possa­vel trocar o mesmo jogador.");

        var match = await _context.Matches
            .Include(m => m.Players)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
            throw new InvalidOperationException("Partida na£o encontrada.");

        var mpA = match.Players.FirstOrDefault(p => p.PlayerId == playerAId);
        var mpB = match.Players.FirstOrDefault(p => p.PlayerId == playerBId);

        if (mpA is null || mpB is null)
            throw new InvalidOperationException("Um ou ambos os jogadores na£o pertencem a esta partida.");

        match.SwapPlayers(mpA.Id, mpB.Id);

        await _context.SaveChangesAsync(ct);
    }


    private async Task<MatchEntity> LoadMatchForSimpleUpdateAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        EnsureMatchId(matchId);

        var match = await _context.Matches
            .Include(m => m.Players)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        return match ?? throw new InvalidOperationException("Partida na£o encontrada.");
    }

    private async Task<MatchEntity?> LoadMatchForSimpleUpdateOrNullAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        EnsureMatchId(matchId);

        return await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);
    }

    private async Task<MatchEntity> LoadMatchForDomainActionsAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        EnsureMatchId(matchId);

        var match = await _context.Matches
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Include(m => m.Players)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(ct);

        return match ?? throw new InvalidOperationException("Partida na£o encontrada.");
    }

    private async Task<MatchEntity> LoadMatchForPlayersUpdateAsync(Guid groupId, Guid matchId, CancellationToken ct)
    {
        EnsureMatchId(matchId);

        var match = await _context.Matches
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Include(m => m.Players)
            .FirstOrDefaultAsync(ct);

        return match ?? throw new InvalidOperationException("Partida na£o encontrada.");
    }

    private async Task SyncPlayersFromGroupCoreAsync(Guid groupId, MatchEntity match, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(match);

        var players = await _context.Players
            .Where(p => p.GroupId == groupId)
            .ToListAsync(ct);

        foreach (var player in players)
        {
            var alreadyInMatch = match.Players.Any(mp => mp.PlayerId == player.Id);
            if (alreadyInMatch) continue;

            var mp = new MatchPlayerEntity(player.Id);
            match.AddPlayer(mp, player);
        }
    }

    private async Task EnsureGroupExistsAsync(Guid groupId, CancellationToken ct)
    {
        EnsureGroupId(groupId);

        var exists = await _context.Groups
            .AsNoTracking()
            .AnyAsync(g => g.Id == groupId, ct);

        if (!exists)
            throw new InvalidOperationException("Group na£o encontrado.");
    }

    private async Task EnsureTeamColorExistsAsync(Guid colorId, string errorMessage, CancellationToken ct)
    {
        var exists = await _context.TeamColors
            .AsNoTracking()
            .AnyAsync(c => c.Id == colorId, ct);

        if (!exists)
            throw new InvalidOperationException(errorMessage);
    }

    private static void EnsureGroupId(Guid groupId)
    {
        if (groupId == Guid.Empty)
            throw new InvalidOperationException("GroupId a© obrigata³rio.");
    }

    private static void EnsureMatchId(Guid matchId)
    {
        if (matchId == Guid.Empty)
            throw new InvalidOperationException("MatchId a© obrigata³rio.");
    }

    private static PlayerInMatchDto ToPlayerDto(MatchPlayerEntity mp) => new()
    {
        MatchPlayerId = mp.Id,
        PlayerId = mp.PlayerId,
        PlayerName = mp.Player?.Name ?? string.Empty,
        IsGoalkeeper = mp.Player?.IsGoalkeeper ?? false,
        Team = mp.Team,
        InviteResponse = (short)mp.InviteResponse
    };
}
