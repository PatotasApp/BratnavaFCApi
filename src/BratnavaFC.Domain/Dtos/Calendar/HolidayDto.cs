namespace BratnavaFC.Domain.Dtos.Calendar;

/// <summary>
/// Representa um feriado nacional retornado pela BrasilAPI.
/// </summary>
public sealed record HolidayDto(
    /// <summary>Data no formato "YYYY-MM-DD".</summary>
    string Date,

    /// <summary>Nome do feriado em português.</summary>
    string Name
);
