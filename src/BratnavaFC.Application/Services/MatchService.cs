using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
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
        EnsureGroupId(groupId);

        return await _context.Matches
            .Where(m => m.GroupId == groupId)
            .Include(m => m.Players).ThenInclude(mp => mp.Player)
            .Include(m => m.Votes)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<MatchEntity?> GetByIdAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken = default)
    {
        EnsureGroupId(groupId);

        return await _context.Matches
            .Where(m => m.GroupId == groupId && m.Id == matchId)
            .Include(m => m.Players).ThenInclude(mp => mp.Player)
            .Include(m => m.Votes)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<MatchEntity> Create(Guid groupId, MatchEntity match, CancellationToken cancellationToken)
    {
        EnsureGroupId(groupId);

        if (match.GroupId != groupId)
            throw new InvalidOperationException("GroupId do match não bate com o GroupId da request.");

        match.CreateDate = DateTime.UtcNow;

        _repository.Add(match);
        await _repository.SaveChangesAsync(cancellationToken);

        await SyncPlayersFromGroupAsync(groupId, match.Id, cancellationToken);

        return match;
    }

    public async Task UpdateAsync(Guid groupId, Guid matchId, UpdateMatchDto dto, CancellationToken cancellationToken)
    {
        EnsureGroupId(groupId);

        var match = await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, cancellationToken);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        match.UpdateDetails(groupId, dto.PlayedAt, dto.PlaceName, matchIdFromRoute: matchId, dtoId: dto.Id);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        EnsureGroupId(groupId);

        var entity = await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, cancellationToken);

        if (entity == null) return;

        entity.EnsureCanDelete();

        _repository.Remove(entity);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task SyncPlayersFromGroupAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        EnsureGroupId(groupId);

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
        EnsureGroupId(groupId);

        var match = await LoadMatchForActions(groupId, matchId, cancellationToken);
        match.AcceptInvite(playerId);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RejectInviteAsync(Guid groupId, Guid matchId, Guid playerId, CancellationToken cancellationToken)
    {
        EnsureGroupId(groupId);

        var match = await LoadMatchForActions(groupId, matchId, cancellationToken);
        match.RejectInvite(playerId);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task StartMatchAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        EnsureGroupId(groupId);

        var match = await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, cancellationToken);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        match.Start();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task EndMatchAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        EnsureGroupId(groupId);

        var match = await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, cancellationToken);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        match.End();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task VoteAsync(Guid groupId, Guid matchId, Guid voterMatchPlayerId, Guid votedMatchPlayerId, CancellationToken cancellationToken)
    {
        EnsureGroupId(groupId);

        var match = await LoadMatchForActions(groupId, matchId, cancellationToken);
        var vote = match.CreateVote(voterMatchPlayerId, votedMatchPlayerId);

        await _context.Votes.AddAsync(vote, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<MatchPlayerEntity?> GetMvpAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken = default)
    {
        EnsureGroupId(groupId);

        var match = await LoadMatchForActions(groupId, matchId, cancellationToken);
        return match.GetComputedMvp();
    }

    public async Task SetScoreAsync(Guid groupId, Guid matchId, int teamAGoals, int teamBGoals, CancellationToken cancellationToken)
    {
        EnsureGroupId(groupId);

        var match = await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, cancellationToken);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        match.SetScore(teamAGoals, teamBGoals);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetTeamColorsAsync(Guid groupId, Guid matchId, Guid? teamAColorId, Guid? teamBColorId, bool randomize, CancellationToken cancellationToken)
    {
        EnsureGroupId(groupId);

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
        EnsureGroupId(groupId);

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

    private static void EnsureGroupId(Guid groupId)
    {
        if (groupId == Guid.Empty)
            throw new InvalidOperationException("GroupId é obrigatório.");
    }
}
