using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.Abstractions;

public interface IMatchService
{
    Task<IEnumerable<MatchEntity>> GetAllAsync();
    Task<MatchEntity?> GetByIdAsync(Guid id);
    Task<MatchEntity> CreateAsync(MatchEntity match);
    Task UpdateAsync(MatchEntity match);
    Task DeleteAsync(Guid id);
}