using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Domain.Abstractions;

public interface IRepositoryBase<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id);
    Task<IEnumerable<T>> GetAllAsync();
    Task AddAsync(T entity);
    void Update(T entity);
    void Remove(T entity);
    Task SaveChangesAsync();
}