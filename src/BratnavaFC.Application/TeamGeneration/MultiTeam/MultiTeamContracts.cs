namespace BratnavaFC.Application.TeamGeneration.MultiTeam;

/// <summary>Estratégias disponíveis para dividir jogadores entre três ou mais times.</summary>
public enum MultiTeamStrategy
{
    Random = 1,
    Balanced = 2,
}

/// <summary>
/// Um time sugerido. Nome e cor são definidos pelo fluxo do campeonato que consumir este DTO.
/// </summary>
public sealed record MultiTeamDto(
    IReadOnlyList<Guid> PlayerIds,
    Guid? GoalkeeperPlayerId,
    double Strength);

/// <summary>Uma composição completa, com a medida usada para comparar o equilíbrio.</summary>
public sealed record MultiTeamDrawOptionDto(
    IReadOnlyList<MultiTeamDto> Teams,
    double StrengthDifference,
    double BaselineStrengthDifference);

/// <summary>
/// Resultado pronto para ser convertido em DefineTeamsDto pelo serviço de campeonato.
/// PlayerStrengths torna explícita a força efetiva, inclusive a média aplicada a quem não tinha nota.
/// </summary>
public sealed record MultiTeamDrawResultDto(
    IReadOnlyList<MultiTeamDrawOptionDto> Options,
    IReadOnlyDictionary<Guid, double> PlayerStrengths);
