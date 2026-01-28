using Microsoft.EntityFrameworkCore;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Abstractions;

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
            .ToListAsync();
    }

    public async Task<MatchEntity?> GetByIdAsync(Guid id)
    {
        return await _context.Matches
            .Include(m => m.Players)
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
}