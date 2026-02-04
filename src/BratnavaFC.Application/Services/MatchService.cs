using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Abstractions;
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

    public async Task UpdateAsync(MatchEntity match, CancellationToken cancellationToken)
    {
        if (match.Status == MatchStatus.Finalized)
            throw new InvalidOperationException("Partida já Finalizada. Não é possível atualizar seus dados.");

        _repository.Update(match);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null) return;

        if (entity.Status == MatchStatus.Finalized)
            throw new InvalidOperationException("Partida já Finalizada. Não é possível excluir.");

        _repository.Remove(entity);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task AcceptInviteAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken)
    {
        var match = await LoadMatchForActions(matchId, cancellationToken);

        EnsureStatus(match, MatchStatus.Created,
            "Só é possível aceitar convite quando a partida está Criada.");

        var mp = FindMatchPlayer(match, playerId);

        mp.InviteResponse = InviteResponse.Accepted;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RejectInviteAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken)
    {
        var match = await LoadMatchForActions(matchId, cancellationToken);

        EnsureStatus(match, MatchStatus.Created,
            "Só é possível recusar convite quando a partida está Criada.");

        var mp = FindMatchPlayer(match, playerId);

        mp.InviteResponse = InviteResponse.Rejected;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task StartMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _context.Matches.FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);
        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        EnsureStatus(match, MatchStatus.Created, "A partida só pode ser iniciada se estiver Criada.");

        match.Start();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task EndMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _context.Matches.FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);
        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        EnsureStatus(match, MatchStatus.Started, "A partida só pode ser encerrada se estiver Iniciada.");

        match.End();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task VoteAsync(Guid matchId, Guid voterPlayerId, Guid votedPlayerId, CancellationToken cancellationToken)
    {
        var match = await LoadMatchForActions(matchId, cancellationToken);

        EnsureStatus(match, MatchStatus.Ended, "Só é possível votar no MVP quando a partida está Encerrada.");

        var voter = match.Players.FirstOrDefault(p => p.Id == voterPlayerId);
        if (voter == null) throw new InvalidOperationException("Apenas jogadores da partida podem votar.");

        var already = match.Votes.Any(v => v.VoterId == voterPlayerId);
        if (already) throw new InvalidOperationException("Esse jogador já votou.");

        var votedFor = match.Players.FirstOrDefault(p => p.Id == votedPlayerId);
        if (votedFor == null) throw new InvalidOperationException("Apenas jogadores que jogaram podem ser votados.");

        var vote = new VoteEntity(match.Id, voter.Id, votedFor.Id);
        await _context.Votes.AddAsync(vote, cancellationToken);

        votedFor.AddReceivedVote(vote);
        voter.SetVotedFor(votedFor.Id);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<MatchPlayerEntity?> GetMvpAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var match = await LoadMatchForActions(matchId, cancellationToken);

        var top = match.Votes
            .Where(v => v.VotedForId != Guid.Empty)
            .GroupBy(v => v.VotedForId)
            .Select(g => new { PlayerId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();

        if (top == null || top.PlayerId == Guid.Empty) return null;

        return match.Players.FirstOrDefault(p => p.Id == top.PlayerId);
    }

    public async Task SetScoreAsync(Guid matchId, int teamAGoals, int teamBGoals, CancellationToken cancellationToken)
    {
        var match = await _context.Matches.FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);
        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        EnsureStatus(match, MatchStatus.Ended, "Só é possível setar placar quando a partida está Encerrada.");

        match.SetScore(teamAGoals, teamBGoals);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetTeamColorsAsync(
        Guid matchId,
        Guid? teamAColorId,
        Guid? teamBColorId,
        bool randomize = false,
        CancellationToken cancellationToken = default)
    {
        var match = await _context.Matches.FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);
        if (match == null) throw new InvalidOperationException("Partida não encontrada.");

        EnsureStatus(match, MatchStatus.Created, "Só é possível setar/sortear cores quando a partida está Criada.");

        if (randomize)
        {
            var colors = await _context.TeamColors.ToListAsync(cancellationToken);
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

        EnsureStatus(match, MatchStatus.Ended, "A partida só pode ser Finalizada se estiver Encerrada.");

        if (!match.TeamAGoals.HasValue || !match.TeamBGoals.HasValue)
            throw new InvalidOperationException("Para finalizar a partida, o placar deve estar definido.");

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

        match.FinalizeMatch();

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

    private static void EnsureStatus(MatchEntity match, MatchStatus required, string message)
    {
        if (match.Status != required)
            throw new InvalidOperationException(message);
    }

    private static MatchPlayerEntity FindMatchPlayer(MatchEntity match, Guid playerId)
    {
        var mp = match.Players.FirstOrDefault(p => p.Id == playerId || p.PlayerId == playerId);

        if (mp == null)
            throw new InvalidOperationException("Jogador não encontrado nesta partida.");

        return mp;
    }
}