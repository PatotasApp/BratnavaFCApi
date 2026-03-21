using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Calendar;

namespace BratnavaFC.Application.Abstractions;

/// <summary>
/// Retorna feriados nacionais do Brasil para um determinado ano,
/// consultando a BrasilAPI com cache em memória.
/// </summary>
public interface IHolidayService
{
    /// <summary>
    /// Retorna os feriados do ano informado.
    /// Em caso de falha na API externa, retorna lista vazia (não propaga exceção).
    /// </summary>
    Task<Result<IReadOnlyList<HolidayDto>>> GetHolidaysAsync(int year, CancellationToken ct = default);
}
