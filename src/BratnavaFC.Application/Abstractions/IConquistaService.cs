using BratnavaFC.Domain.Dtos.Conquistas;

namespace BratnavaFC.Application.Abstractions;

public interface IConquistaService
{
    /// <summary>Calcula as conquistas de todos os jogadores do grupo (raridade/percentil relativos à patota).</summary>
    Task<GroupConquistasDto> GetGroupConquistasAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>Como acima, com o "na temporada" da temporada escolhida (padrão: a atual).</summary>
    Task<GroupConquistasDto> GetGroupConquistasAsync(Guid groupId, Guid? seasonId, CancellationToken ct = default);
}
