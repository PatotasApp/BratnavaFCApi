using BratnavaFC.Domain.Championships;
using System.Text.Json.Serialization;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Domain.Models;

namespace BratnavaFC.Domain.Dtos.Championships;

// Contrato: docs/campeonato/CONTRATO-API.md. Mudou aqui, mude lá (e anote no histórico).

public sealed record CreateChampionshipDto(string Name, DateTime PlayedAt, string PlaceName, ChampionshipRuleSet? Rules);

public sealed record UpdateChampionshipDto(string Name, DateTime PlayedAt, string PlaceName, ChampionshipRuleSet Rules);

public sealed record ChampionshipPlayerActionDto(Guid PlayerId);

/// <summary>
/// Convidado do campeonato: <c>PlayerId</c> de um jogador já cadastrado na patota, ou um
/// convidado novo pelo nome (como na partida), criado na patota e já confirmado.
/// </summary>
public sealed record AddChampionshipGuestDto(Guid? PlayerId, string? Name, bool IsGoalkeeper = false, int? GuestStarRating = null);

public sealed record DrawTeamsDto(int TeamCount);

/// <summary>
/// Sorteio do campeonato (v8) com os parâmetros da partida mais a quantidade de times.
/// <c>ColorIds</c> são as cores escolhidas na aba Cores, na ordem dos times; vazio sorteia.
/// </summary>
public sealed record ChampionshipDrawRequestDto(
    int TeamCount,
    StrategyType StrategyType = StrategyType.Algorithm,
    bool IncludeGoalkeepers = true,
    List<Guid?>? ColorIds = null,
    int OptionsCount = 3);

/// <summary>Uma opção do sorteio, já no formato do <c>PUT /teams</c>.</summary>
public sealed record ChampionshipDrawOptionDto(
    List<DefineTeamDto> Teams,
    List<Guid> UnassignedPlayerIds,
    List<double> TeamStrengths,
    double StrengthDifference);

public sealed record ChampionshipDrawOptionsDto(List<ChampionshipDrawOptionDto> Options);

public sealed record SetChampionshipPlayerGoalkeeperDto(bool IsGoalkeeper);

public sealed record DefineTeamDto(string Name, Guid? ColorId, List<Guid> PlayerIds, Guid? GoalkeeperPlayerId);

public sealed record DefineTeamsDto(List<DefineTeamDto> Teams);

public sealed record MovePlayerDto(Guid? TeamId);

public sealed record UpdateChampionshipTeamDto(string Name, Guid? ColorId);

public sealed record CreateChampionshipGameDto(Guid TeamAId, Guid TeamBId);

public sealed record ChampionshipGoalDto(Guid ScorerPlayerId, Guid? AssistPlayerId, bool IsOwnGoal, int? TimeSeconds);

public sealed record SetGameGoalkeeperDto(Guid TeamId, Guid PlayerId);

public sealed record EndChampionshipGameDto(Guid? ExternalWinnerTeamId);

/// <summary>Lance para as câmeras: GolTimeA/GolTimeB (times A/B do jogo) ou Jogada.</summary>
public sealed record ChampionshipGameEventRequestDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MatchEventType Type,
    int SecondsBeforeStart = 15,
    int DurationSeconds = 15,
    DateTimeOffset? EventTime = null);

public sealed record ChampionshipGameEventCreatedDto(Guid Id, string QueueId, Guid? TeamId, DateTime EventTime, int? ElapsedSeconds);

public sealed record ChampionshipVoteDto(Guid VotedPlayerId, Guid? GameId);

public sealed record FinalizeChampionshipDto(List<Guid>? ExternalDecisionTeamOrder);

public sealed record ChampionshipCreatedDto(Guid Id);

public sealed record ChampionshipGameCreatedDto(Guid GameId);

public sealed record ChampionshipSummaryDto(
    Guid Id,
    string Name,
    DateTime PlayedAt,
    string PlaceName,
    ChampionshipStatus Status,
    int TeamCount,
    int GameCount,
    string? ChampionTeamName,
    string? ChampionTeamColorHex,
    IReadOnlyList<string> RuleBadges);

public sealed record ChampionshipParticipantDto(
    Guid PlayerId,
    string Name,
    string? PhotoUrl,
    bool IsGuest,
    bool IsGoalkeeper,
    InviteResponse InviteResponse,
    Guid? TeamId,
    bool DidNotPlay);

public sealed record ChampionshipTeamDto(
    Guid Id,
    string Name,
    Guid? ColorId,
    string? ColorHex,
    int? FinalPosition,
    IReadOnlyList<Guid> PlayerIds);

public sealed record ChampionshipGameGoalDto(
    Guid Id,
    Guid ScorerPlayerId,
    Guid? AssistPlayerId,
    bool IsOwnGoal,
    int? TimeSeconds);

public enum ChampionshipGameStatus
{
    InProgress = 1,
    Finished = 2
}

public sealed record ChampionshipGameDto(
    Guid Id,
    int Order,
    Guid TeamAId,
    Guid TeamBId,
    int TeamAGoals,
    int TeamBGoals,
    Guid? ExternalWinnerTeamId,
    ChampionshipGameStatus Status,
    DateTime? StartedAt,
    IReadOnlyList<ChampionshipGameGoalDto> Goals);

public sealed record ChampionshipStandingDto(
    int Position,
    Guid TeamId,
    int Played,
    int Wins,
    int Draws,
    int Losses,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    int Points,
    decimal PointsPerGame);

public sealed record ChampionshipScorerDto(Guid PlayerId, int Goals);

public sealed record ChampionshipAssistDto(Guid PlayerId, int Assists);

public sealed record ChampionshipMyVoteDto(Guid? GameId, Guid VotedPlayerId);

public sealed record ChampionshipDetailsDto(
    Guid Id,
    string Name,
    DateTime PlayedAt,
    string PlaceName,
    ChampionshipStatus Status,
    ChampionshipRuleSet Rules,
    bool RulesLocked,
    IReadOnlyList<string> RuleBadges,
    IReadOnlyList<ChampionshipParticipantDto> Players,
    IReadOnlyList<ChampionshipTeamDto> Teams,
    IReadOnlyList<ChampionshipGameDto> Games,
    IReadOnlyList<ChampionshipStandingDto> Standings,
    Guid? CurrentGameId,
    IReadOnlyList<Guid> WaitingTeamIds,
    IReadOnlyList<ChampionshipScorerDto> TopScorers,
    IReadOnlyList<ChampionshipAssistDto> TopAssists,
    IReadOnlyList<Guid> MvpPlayerIds,
    InviteResponse? MyInviteResponse,
    bool CanVote,
    IReadOnlyList<ChampionshipMyVoteDto> MyVotes,
    Guid? LinkedPollId = null);

public sealed record NextGameSuggestionDto(
    Guid TeamAId,
    Guid TeamBId,
    string Reason,
    IReadOnlyList<Guid> WaitingTeamIds);
