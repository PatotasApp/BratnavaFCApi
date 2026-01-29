using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Abstractions;
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

    public async Task<IEnumerable<MatchEntity>> GetAllAsync()
    {
        return await _context.Matches
            .Include(m => m.Players)
            .Include(m => m.Votes)
            .ToListAsync();
    }

    public async Task<MatchEntity?> GetByIdAsync(Guid id)
    {
        return await _context.Matches
            .Include(m => m.Players)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<MatchEntity> CreateAsync(MatchEntity match)
    {
        match.CreateDate = DateTime.UtcNow;
        await _repository.AddAsync(match);
        await _repository.SaveChangesAsync();
        return match;
    }

    public async Task UpdateAsync(MatchEntity match)
    {
        match.UpdateDate = DateTime.UtcNow;
        _repository.Update(match);
        await _repository.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity == null) return;
        _repository.Remove(entity);
        await _repository.SaveChangesAsync();
    }

    public async Task VoteAsync(Guid matchId, Guid voterPlayerId, Guid votedPlayerId)
    {
        var match = await _context.Matches
            .Include(m => m.Players)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(m => m.Id == matchId);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        if (match.IsFinalized) throw new InvalidOperationException("Partida já finalizada.");

        var voter = match.Players.FirstOrDefault(p => p.Id == voterPlayerId);
        if (voter == null) throw new InvalidOperationException("Apenas jogadores da partida podem votar.");

        var already = match.Votes.Any(v => v.VoterId == voterPlayerId);
        if (already) throw new InvalidOperationException("Esse jogador já votou.");

        var votedFor = match.Players.FirstOrDefault(p => p.Id == votedPlayerId);
        if (votedFor == null) throw new InvalidOperationException("Apenas jogadores que jogaram podem ser votados.");

        var vote = new VoteEntity(match.Id, voter.Id, votedFor.Id);
        await _context.Votes.AddAsync(vote);

        votedFor.AddReceivedVote(vote);
        voter.SetVotedFor(votedFor.Id);

        await _context.SaveChangesAsync();
    }

    public async Task FinalizeMatchAsync(Guid matchId)
    {
        var match = await _context.Matches
            .Include(m => m.Players)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(m => m.Id == matchId);

        if (match == null) throw new InvalidOperationException("Partida não encontrada.");
        if (match.IsFinalized) throw new InvalidOperationException("Partida já finalizada.");

        var top = match.Votes
            .Where(v => v.VotedForId != Guid.Empty)
            .GroupBy(v => v.VotedForId)
            .Select(g => new { PlayerId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();

        match.Players.ForEach(e => e.RevokeMvp());

        if (top != null && top.PlayerId != Guid.Empty)
        {
            var winner = match.Players.FirstOrDefault(p => p.Id == top.PlayerId);
            winner?.SetMvp();
        }

        match.MarkFinalized();
        await _context.SaveChangesAsync();
    }

    public async Task<MatchPlayerEntity?> GetMvpAsync(Guid matchId)
    {
        var match = await _context.Matches
            .Include(m => m.Players)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(m => m.Id == matchId);

        if (match == null) return null;

        var top = match.Votes
            .Where(v => v.VotedForId != Guid.Empty)
            .GroupBy(v => v.VotedForId)
            .Select(g => new { PlayerId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();

        if (top == null || top.PlayerId == Guid.Empty) return null;

        return match.Players.FirstOrDefault(p => p.Id == top.PlayerId);
    }
}