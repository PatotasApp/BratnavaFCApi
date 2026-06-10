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

    /// <summary>
    /// Retorna apenas o prompt que seria enviado ao OpenAI — sem chamar a API.
    /// Útil para copiar e usar em ferramentas externas (ChatGPT, Midjourney, etc.).
    /// </summary>
    Result<string> GetPrompt(GenerateMatchCardDto dto);
}
