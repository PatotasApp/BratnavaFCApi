using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Models;

namespace BratnavaFC.Application.Abstractions;

public interface ISpotlightService
{
    /// <summary>Configuração do Spotlight da patota (padrão se nunca foi salva).</summary>
    Task<Result<SpotlightSettings>> GetSettingsAsync(Guid groupId, CancellationToken ct = default);

    Task<Result<SpotlightSettings>> SaveSettingsAsync(Guid groupId, SpotlightSettings settings, CancellationToken ct = default);

    /// <summary>Spotlight do telão com o período e os filtros salvos (só admins chegam aqui).</summary>
    Task<Result<SpotlightEventReport>> GetReportAsync(Guid groupId, CancellationToken ct = default);
}
