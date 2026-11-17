using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;

namespace BratnavaFC.Application.Abstractions;

public interface IPuskasVotingService
{
    /// <summary>Votação atual da patota, ou null se ainda não foi criada.</summary>
    Task<Result<PuskasPollDto?>> GetPollAsync(Guid groupId, CancellationToken ct);

    /// <summary>Cria a votação (ou, sem votos ainda, atualiza a lista) com o top 10 atual. Exige todos os autores informados.</summary>
    /// <param name="countdown">Ordem do telão: do último ao 1º (true) ou do 1º ao último; a folha de votação segue a mesma.</param>
    Task<Result<PuskasPollDto>> OpenPollAsync(Guid groupId, bool countdown, CancellationToken ct);

    Task<Result<PuskasPollDto>> SetOpenAsync(Guid groupId, bool isOpen, CancellationToken ct);

    Task<Result<PuskasResultsDto>> GetResultsAsync(Guid groupId, CancellationToken ct);

    /// <summary>Folha de votação pública, pelo código do QR code.</summary>
    Task<Result<PuskasSheetDto>> GetSheetAsync(string code, CancellationToken ct);

    Task<Result> SubmitVoteAsync(string code, string voterName, IReadOnlyList<Guid> clipIds, CancellationToken ct);
}
