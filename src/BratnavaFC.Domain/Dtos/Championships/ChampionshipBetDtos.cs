using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Championships;

// ── Pedido ───────────────────────────────────────────────────────────────────

/// <summary>PredictedId: id do time (Champion) ou do jogador (demais categorias).</summary>
public sealed record PlaceChampionshipBetSelectionDto(ChampionshipBetCategory Category, Guid PredictedId, int FichasWagered);

public sealed record PlaceChampionshipBetDto(List<PlaceChampionshipBetSelectionDto> Selections);

// ── Resposta ─────────────────────────────────────────────────────────────────

/// <summary>Campeonato aceitando apostas (entra no seletor da aba "Aposta Atual" com 🏆).</summary>
public sealed record ChampionshipBettableDto(Guid ChampionshipId, string Name, DateTime PlayedAt, string PlaceName);

/// <summary>Uma opção de aposta: um time (Champion) ou um jogador.</summary>
public sealed record ChampionshipBetOptionDto(Guid Id, string Name, string? ColorHex, Guid? TeamId);

/// <summary>
/// Uma categoria disponível. Multiplier: quanto ganha sobre o valor apostado se acertar.
/// WinnerIds: vencedores da categoria depois de liquidada (empate = vários; ninguém = vazio).
/// </summary>
public sealed record ChampionshipBetCategoryDto(
    ChampionshipBetCategory Category,
    decimal Multiplier,
    IReadOnlyList<ChampionshipBetOptionDto> Options,
    IReadOnlyList<Guid>? WinnerIds);

public sealed record ChampionshipBetSelectionDto(
    ChampionshipBetCategory Category,
    Guid PredictedId,
    string PredictedName,
    int FichasWagered,
    int? FichasEarned,
    bool? IsCorrect);

/// <summary>TotalForChampionship: bônus de participação + ganhos/perdas (só depois de liquidada).</summary>
public sealed record ChampionshipUserBetDto(
    Guid UserId,
    string UserName,
    bool IsResolved,
    IReadOnlyList<ChampionshipBetSelectionDto> Selections,
    int? TotalForChampionship);

/// <summary>
/// Tudo para a tela de aposta do campeonato. AllBets só vem com as apostas fechadas
/// (campeonato iniciado), para ninguém copiar a aposta dos outros.
/// </summary>
public sealed record ChampionshipBetContextDto(
    Guid ChampionshipId,
    string Name,
    DateTime PlayedAt,
    bool IsOpen,
    string? ClosedReason,
    bool IsResolved,
    int MinWagerPerCategory,
    int MaxWagerTotal,
    int ParticipationBonus,
    int MyBalance,
    IReadOnlyList<ChampionshipBetCategoryDto> Categories,
    ChampionshipUserBetDto? MyBet,
    IReadOnlyList<ChampionshipUserBetDto> AllBets);

/// <summary>Item do histórico de apostas: um campeonato liquidado com as apostas de todos.</summary>
public sealed record ChampionshipBetHistoryDto(
    Guid ChampionshipId,
    string Name,
    DateTime PlayedAt,
    IReadOnlyList<ChampionshipUserBetDto> UserBets);
