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

    public async Task<IEnumerable<TeamColorEntity>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<TeamColorEntity?> GetByIdAsync(Guid id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<TeamColorEntity> CreateAsync(TeamColorEntity color)
    {
        if (color == null) throw new ArgumentNullException(nameof(color));
        await _repository.AddAsync(color);
        await _repository.SaveChangesAsync();
        return color;
    }

    public async Task UpdateAsync(TeamColorEntity color)
    {
        if (color == null) throw new ArgumentNullException(nameof(color));
        _repository.Update(color);
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