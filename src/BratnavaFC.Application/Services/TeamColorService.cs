using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.Services;

public sealed class TeamColorService : ITeamColorService
{
    private readonly IRepositoryBase<TeamColorEntity> _repository;

    public TeamColorService(IRepositoryBase<TeamColorEntity> repository)
    {
        _repository = repository;
    }

    public Task<IEnumerable<TeamColorEntity>> GetAllAsync(CancellationToken ct)
        => _repository.GetAllAsync(ct);

    public Task<TeamColorEntity?> GetByIdAsync(Guid id, CancellationToken ct)
        => _repository.GetByIdAsync(id, ct);

    public async Task<TeamColorEntity> CreateAsync(CreateTeamColorDto dto, CancellationToken ct)
    {
        var entity = new TeamColorEntity(dto.Name, dto.HexValue);

        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);

        return entity;
    }

    public async Task UpdateAsync(Guid id, UpdateTeamColorDto dto, CancellationToken ct)
    {
        if (id == Guid.Empty)
            throw new InvalidOperationException("Id é obrigatório.");

        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw new InvalidOperationException("Cor não encontrada.");

        entity.Update(dto.Name, dto.HexValue);

        _repository.Update(entity);
        await _repository.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
            throw new InvalidOperationException("Id é obrigatório.");

        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null) return;

        _repository.Remove(entity);
        await _repository.SaveChangesAsync(ct);
    }
}