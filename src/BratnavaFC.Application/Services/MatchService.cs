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

    public async Task<IEnumerable<MatchEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Matches
            .Include(m => m.Players)
            .Include(m => m.Votes)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<MatchEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Matches
            .Include(m => m.Players)
            .Include(m => m.Votes)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<MatchEntity> Create(MatchEntity match, CancellationToken cancellationToken)
    {
        match.CreateDate = DateTime.UtcNow;

        _repository.Add(match);
        await _repository.SaveChangesAsync(cancellationToken);

        return match;
    }

    public async Task UpdateAsync(Guid matchId, UpdateMatchDto dto, CancellationToken cancellationToken)
    {
        var match = await _context.Matches.FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);
        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        match.UpdateDetails(dto.PlayedAt, dto.PlaceName, matchIdFromRoute: matchId, dtoId: dto.Id);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null) return;

        entity.EnsureCanDelete();

        _repository.Remove(entity);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task AcceptInviteAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken)
    {
        var match = await LoadMatchForActions(matchId, cancellationToken);
        match.AcceptInvite(playerId);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RejectInviteAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken)
    {
        var match = await LoadMatchForActions(matchId, cancellationToken);
        match.RejectInvite(playerId);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task StartMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _context.Matches.FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);
        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        match.Start();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task EndMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _context.Matches.FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);
        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        match.End();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task VoteAsync(Guid matchId, Guid voterMatchPlayerId, Guid votedMatchPlayerId, CancellationToken cancellationToken)
    {
        var match = await LoadMatchForActions(matchId, cancellationToken);

        var vote = match.CreateVote(voterMatchPlayerId, votedMatchPlayerId);

        await _context.Votes.AddAsync(vote, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<MatchPlayerEntity?> GetMvpAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var match = await LoadMatchForActions(matchId, cancellationToken);
        return match.GetComputedMvp();
    }

    public async Task SetScoreAsync(Guid matchId, int teamAGoals, int teamBGoals, CancellationToken cancellationToken)
    {
        var match = await _context.Matches.FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);
        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        match.SetScore(teamAGoals, teamBGoals);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetTeamColorsAsync(Guid matchId, Guid? teamAColorId, Guid? teamBColorId, bool randomize, CancellationToken cancellationToken)
    {
        var match = await _context.Matches.FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);
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

    public async Task FinalizeMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await LoadMatchForActions(matchId, cancellationToken);

        match.FinalizeByVotes();

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<MatchEntity> LoadMatchForActions(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _context.Matches
            .Include(m => m.Players)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");
        return match;
    }
}
