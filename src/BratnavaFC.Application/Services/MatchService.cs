using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public class MatchService : IMatchService
{
    private readonly AppDbContext _context;
    private readonly IRepositoryBase<MatchEntity> _repository;

    public MatchService(AppDbContext context, IRepositoryBase<MatchEntity> repository)
    {
        _context = context;
        _repository = repository;
    }

    public async Task<IEnumerable<MatchEntity>> GetAllAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        return await _context.Matches
            .Where(m => m.GroupId == groupId)
            .Include(m => m.Players).ThenInclude(mp => mp.Player)
            .Include(m => m.Votes)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<MatchEntity?> GetByIdAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken = default)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        return await _context.Matches
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Include(m => m.Players).ThenInclude(mp => mp.Player)
            .Include(m => m.Votes)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<MatchEntity> Create(Guid groupId, MatchEntity match, CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        _repository.Add(match);

        await SyncPlayersFromGroupAsync(groupId, match, cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        return match;
    }


    public async Task UpdateAsync(Guid groupId, Guid matchId, UpdateMatchDto dto, CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        var match = await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, cancellationToken);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        match.UpdateDetails(groupId, dto.PlayedAt, dto.PlaceName, matchIdFromRoute: matchId, dtoId: dto.Id);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        var entity = await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, cancellationToken);

        if (entity == null) return;

        entity.EnsureCanDelete();

        _repository.Remove(entity);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task SyncPlayersFromGroupAsync(Guid groupId, MatchEntity match, CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        var players = await _context.Players
            .Where(p => p.GroupId == groupId)
            .ToListAsync(cancellationToken);

        foreach (var player in players)
        {
            if (match.Players.Any(mp => mp.PlayerId == player.Id))
                continue;

            var mp = new MatchPlayerEntity(player.Id);
            match.AddPlayer(mp, player);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SyncPlayersFromGroupAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        var match = await _context.Matches
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Include(m => m.Players)
            .FirstOrDefaultAsync(cancellationToken);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        var players = await _context.Players
            .Where(p => p.GroupId == groupId)
            .ToListAsync(cancellationToken);

        foreach (var player in players)
        {
            if (match.Players.Any(mp => mp.PlayerId == player.Id))
                continue;

            var mp = new MatchPlayerEntity(player.Id);
            match.AddPlayer(mp, player);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AcceptInviteAsync(Guid groupId, Guid matchId, Guid playerId, CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        var match = await LoadMatchForActions(groupId, matchId, cancellationToken);
        match.AcceptInvite(playerId);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RejectInviteAsync(Guid groupId, Guid matchId, Guid playerId, CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        var match = await LoadMatchForActions(groupId, matchId, cancellationToken);
        match.RejectInvite(playerId);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task StartMatchAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        var match = await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, cancellationToken);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        match.Start();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task EndMatchAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        var match = await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, cancellationToken);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        match.End();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task VoteAsync(Guid groupId, Guid matchId, Guid voterMatchPlayerId, Guid votedMatchPlayerId, CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        var match = await LoadMatchForActions(groupId, matchId, cancellationToken);
        var vote = match.CreateVote(voterMatchPlayerId, votedMatchPlayerId);

        await _context.Votes.AddAsync(vote, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<MatchPlayerEntity?> GetMvpAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken = default)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        var match = await LoadMatchForActions(groupId, matchId, cancellationToken);
        return match.GetComputedMvp();
    }

    public async Task SetScoreAsync(Guid groupId, Guid matchId, int teamAGoals, int teamBGoals, CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        var match = await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, cancellationToken);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        match.SetScore(teamAGoals, teamBGoals);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetTeamColorsAsync(Guid groupId, Guid matchId, Guid? teamAColorId, Guid? teamBColorId, bool randomize, CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        var match = await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, cancellationToken);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        if (randomize)
        {
            var colors = await _context.TeamColors.ToListAsync(cancellationToken);
            match.SetTeamColorsRandomly(colors);
        }
        else
        {
            if (teamAColorId.HasValue)
            {
                var foundA = await _context.TeamColors.FindAsync([teamAColorId.Value], cancellationToken: cancellationToken);
                if (foundA == null) throw new InvalidOperationException("Cor do time A não encontrada.");
            }

            if (teamBColorId.HasValue)
            {
                var foundB = await _context.TeamColors.FindAsync([teamBColorId.Value], cancellationToken: cancellationToken);
                if (foundB == null) throw new InvalidOperationException("Cor do time B não encontrada.");
            }

            match.SetTeamColors(teamAColorId, teamBColorId);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task FinalizeMatchAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(groupId, cancellationToken);

        var match = await LoadMatchForActions(groupId, matchId, cancellationToken);
        match.FinalizeByVotes();

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<MatchEntity> LoadMatchForActions(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _context.Matches
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Include(m => m.Players)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(cancellationToken);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");
        return match;
    }

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


    public async Task<MatchDetailsDto?> GetDetailsAsync(Guid matchId, CancellationToken ct)
    {
        var match = await  _context.Matches
            .AsNoTracking()
            .Include(m => m.Group)
            .Include(m => m.TeamAColor)
            .Include(m => m.TeamBColor)
            .Include(m => m.Players)
                .ThenInclude(mp => mp.Player)
            .Include(m => m.Votes)
                .ThenInclude(v => v.Voter)     // MatchPlayerEntity
            .Include(m => m.Votes)
                .ThenInclude(v => v.VotedFor)  // MatchPlayerEntity
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);

        if (match is null) return null;

        // MVP computado por votos (teu método)
        var computedMvp = match.GetComputedMvp();

        // Map rápido de MatchPlayerId -> nome (pra votos)
        var mpName = match.Players.ToDictionary(
            p => p.Id,
            p => p.Player?.Name ?? ""
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
                VotedForName = mpName.TryGetValue(g.Key, out var n) ? n : "",
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.VotedForName)
            .ToList();

        return new MatchDetailsDto
        {
            MatchId = match.Id,
            GroupName = match.Group?.Name ?? "",
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
                    PlayerName = computedMvp.Player?.Name ?? "",
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
                VoterName = mpName.TryGetValue(v.VoterId, out var voterName) ? voterName : "",
                VotedForName = mpName.TryGetValue(v.VotedForId, out var votedName) ? votedName : ""
            }).ToList(),

            VoteCounts = voteCounts,
        };
    }
    public async Task AssignTeamsAsync(Guid groupId, Guid matchId, AssignTeamsDto dto, CancellationToken ct)
    {
        if (groupId == Guid.Empty) throw new InvalidOperationException("GroupId é obrigatório.");
        if (matchId == Guid.Empty) throw new InvalidOperationException("MatchId é obrigatório.");

        dto.TeamAMatchPlayerIds ??= [];
        dto.TeamBMatchPlayerIds ??= [];

        var match = await _context.Matches
            .Include(m => m.Players)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct);

        if (match is null)
            throw new InvalidOperationException("Partida não encontrada.");

        match.AssignTeams(dto.TeamAMatchPlayerIds, dto.TeamBMatchPlayerIds);

        await _context.SaveChangesAsync(ct);
    }

    private static PlayerInMatchDto ToPlayerDto(dynamic mp) => new PlayerInMatchDto
    {
        MatchPlayerId = mp.Id,
        PlayerId = mp.PlayerId,
        PlayerName = mp.Player?.Name ?? "",
        IsGoalkeeper = mp.Player?.IsGoalkeeper ?? false,
        Team = mp.Team,
        InviteResponse = (short)mp.InviteResponse
    };
}
