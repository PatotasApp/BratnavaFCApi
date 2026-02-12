using BratnavaFC.Domain.Entities;

public interface IRepositoryBase<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<T?> GetByIdIncludingInactiveAsync(Guid id, CancellationToken cancellationToken); 

    void Add(T entity);
    void Update(T entity);
    void Remove(T entity);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
