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

    public async Task<MatchEntity> CreateAsync(MatchEntity match, CancellationToken cancellationToken)
    {
        match.CreateDate = DateTime.UtcNow;
        _repository.Add(match);
        await _repository.SaveChangesAsync(cancellationToken);
        return match;
    }

    public async Task UpdateAsync(MatchEntity match, CancellationToken cancellationToken)
    {
        if (match.IsFinalized)
            throw new InvalidOperationException("Partida já finalizada. Não é possível atualizar seus dados.");

        _repository.Update(match);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null) return;
        _repository.Remove(entity);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task VoteAsync(Guid matchId, Guid voterPlayerId, Guid votedPlayerId, CancellationToken cancellationToken)
    {
        var match = await _context.Matches
            .Include(m => m.Players)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(m => m.Id == matchId);

        if (match == null) throw new InvalidOperationException("Partida n�o encontrada.");

        if (match.IsFinalized) throw new InvalidOperationException("Partida j� finalizada.");

        var voter = match.Players.FirstOrDefault(p => p.Id == voterPlayerId);
        if (voter == null) throw new InvalidOperationException("Apenas jogadores da partida podem votar.");

        var already = match.Votes.Any(v => v.VoterId == voterPlayerId);
        if (already) throw new InvalidOperationException("Esse jogador j� votou.");

        var votedFor = match.Players.FirstOrDefault(p => p.Id == votedPlayerId);
        if (votedFor == null) throw new InvalidOperationException("Apenas jogadores que jogaram podem ser votados.");

        var vote = new VoteEntity(match.Id, voter.Id, votedFor.Id);
        await _context.Votes.AddAsync(vote);

        votedFor.AddReceivedVote(vote);
        voter.SetVotedFor(votedFor.Id);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task FinalizeMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _context.Matches
            .Include(m => m.Players)
            .Include(m => m.Votes)
            .FirstOrDefaultAsync(m => m.Id == matchId);

        if (match == null) throw new InvalidOperationException("Partida n�o encontrada.");
        if (match.IsFinalized) throw new InvalidOperationException("Partida j� finalizada.");

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
        await _context.SaveChangesAsync(cancellationToken);
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

    public async Task SetScoreAsync(Guid matchId, int teamAGoals, int teamBGoals, CancellationToken cancellationToken = default)
    {
        var match = await _context.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
        if (match == null) throw new InvalidOperationException("Partida no encontrada.");
        if (match.IsFinalized) throw new InvalidOperationException("Partida j finalizada.");
        match.SetScore(teamAGoals, teamBGoals);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetTeamColorsAsync(Guid matchId, Guid? teamAColorId, Guid? teamBColorId, bool randomize = false, CancellationToken cancellationToken = default)
    {
        var match = await _context.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
        if (match == null) throw new InvalidOperationException("Partida não encontrada.");
        if (match.IsFinalized) throw new InvalidOperationException("Partida já finalizada.");

        if (randomize)
        {
            var colors = await _context.TeamColors.ToListAsync();
            if (colors.Count == 0) throw new InvalidOperationException("Não há cores cadastradas para sortear.");

            var rng = Random.Shared;
            var shuffled = colors.OrderBy(_ => rng.Next()).ToList();
            var a = shuffled[0].Id;
            var b = shuffled.Count > 1 ? shuffled[1].Id : shuffled[0].Id;

            match.SetTeamColors(a, b);
        }
        else
        {
            if (teamAColorId.HasValue)
            {
                var foundA = await _context.TeamColors.FindAsync(teamAColorId.Value);
                if (foundA == null) throw new InvalidOperationException("Cor do time A não encontrada.");
            }

            if (teamBColorId.HasValue)
            {
                var foundB = await _context.TeamColors.FindAsync(teamBColorId.Value);
                if (foundB == null) throw new InvalidOperationException("Cor do time B não encontrada.");
            }

            match.SetTeamColors(teamAColorId, teamBColorId);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}