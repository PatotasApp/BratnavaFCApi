using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Abstractions;

namespace BratnavaFC.Application.Services;

public class TeamColorService : ITeamColorService
{
    private readonly IRepositoryBase<TeamColorEntity> _repository;

    public TeamColorService(IRepositoryBase<TeamColorEntity> repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IEnumerable<TeamColorEntity>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _repository.GetAllAsync(cancellationToken);
    }

    public async Task<TeamColorEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<TeamColorEntity> CreateAsync(TeamColorEntity color, CancellationToken cancellationToken)
    {
        if (color == null) throw new ArgumentNullException(nameof(color));
        await _repository.AddAsync(color, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return color;
    }

    public async Task UpdateAsync(TeamColorEntity color, CancellationToken cancellationToken)
    {
        if (color == null) throw new ArgumentNullException(nameof(color));
        _repository.Update(color);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null) return;
        _repository.Remove(entity);
        await _repository.SaveChangesAsync(cancellationToken);
    }
}