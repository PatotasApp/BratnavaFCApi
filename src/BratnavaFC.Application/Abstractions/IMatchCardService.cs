using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.MatchCard;

namespace BratnavaFC.Application.Abstractions;

public interface IMatchCardService
{
    /// <summary>
    /// Gera um card de partida (preview ou resultado) usando a API de imagem do ChatGPT.
    /// Retorna a imagem em base64 (PNG).
    /// </summary>
    Task<Result<string>> GenerateCardAsync(GenerateMatchCardDto dto, CancellationToken ct = default);
}
