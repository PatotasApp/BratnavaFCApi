using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Seasons;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.Abstractions;

public interface ISeasonService
{
    Task<Result<SeasonsOverviewDto>> ListAsync(Guid groupId, CancellationToken ct);
    Task<Result<SeasonClosePreviewDto>> PreviewCloseAsync(Guid groupId, DateTimeOffset? cutAt, CancellationToken ct);
    Task<Result> CloseAsync(Guid groupId, Guid? userId, CloseSeasonDto dto, CancellationToken ct);
    Task<Result> UpdateAsync(Guid groupId, Guid seasonId, UpdateSeasonDto dto, CancellationToken ct);
    Task<Result> MoveStartAsync(Guid groupId, Guid seasonId, MoveSeasonStartDto dto, CancellationToken ct);
    Task<Result> UndoCloseAsync(Guid groupId, CancellationToken ct);

    /// <summary>Temporadas da patota (garante a cobertura e a virada do ano antes).</summary>
    Task<IReadOnlyList<SeasonEntity>> GetSeasonsAsync(Guid groupId, CancellationToken ct);
}
