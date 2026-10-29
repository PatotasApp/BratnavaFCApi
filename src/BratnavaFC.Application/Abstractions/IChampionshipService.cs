using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Championships;

namespace BratnavaFC.Application.Abstractions;

/// <summary>Modo Campeonato. Contrato HTTP em docs/campeonato/CONTRATO-API.md.</summary>
public interface IChampionshipService
{
    Task<Result<List<ChampionshipSummaryDto>>> ListAsync(Guid groupId, CancellationToken ct);
    Task<Result<ChampionshipDetailsDto>> GetAsync(Guid groupId, Guid championshipId, Guid? userId, CancellationToken ct);
    Task<Result<NextGameSuggestionDto?>> GetNextGameAsync(Guid groupId, Guid championshipId, CancellationToken ct);

    Task<Result<ChampionshipCreatedDto>> CreateAsync(Guid groupId, CreateChampionshipDto dto, CancellationToken ct);
    Task<Result> UpdateAsync(Guid groupId, Guid championshipId, UpdateChampionshipDto dto, CancellationToken ct);
    Task<Result> CancelAsync(Guid groupId, Guid championshipId, CancellationToken ct);

    Task<Result> OpenAcceptationAsync(Guid groupId, Guid championshipId, CancellationToken ct);
    Task<Result> RespondMyInviteAsync(Guid groupId, Guid championshipId, Guid userId, bool accept, CancellationToken ct);
    Task<Result> RespondInviteAsync(Guid groupId, Guid championshipId, Guid playerId, bool accept, CancellationToken ct);
    Task<Result> AddGuestAsync(Guid groupId, Guid championshipId, AddChampionshipGuestDto dto, CancellationToken ct);

    Task<Result<DefineTeamsDto>> DrawTeamsAsync(Guid groupId, Guid championshipId, int teamCount, CancellationToken ct);
    Task<Result<ChampionshipReplaysDto>> GetReplaysAsync(Guid? groupId, Guid championshipId, CancellationToken ct);
    Task<Result> SetLinkedPollAsync(Guid groupId, Guid championshipId, Guid? pollId, CancellationToken ct);
    Task<Result> OpenTeamDrawAsync(Guid groupId, Guid championshipId, CancellationToken ct);
    Task<Result> SetPlayerGoalkeeperAsync(Guid groupId, Guid championshipId, Guid playerId, bool isGoalkeeper, CancellationToken ct);
    Task<Result<ChampionshipDrawOptionsDto>> DrawTeamOptionsAsync(Guid groupId, Guid championshipId, ChampionshipDrawRequestDto dto, CancellationToken ct);
    Task<Result> DefineTeamsAsync(Guid groupId, Guid championshipId, DefineTeamsDto dto, CancellationToken ct);
    Task<Result> MovePlayerAsync(Guid groupId, Guid championshipId, Guid playerId, Guid? teamId, CancellationToken ct);
    Task<Result> DrawTeamColorsAsync(Guid groupId, Guid championshipId, CancellationToken ct);
    Task<Result> UpdateTeamAsync(Guid groupId, Guid championshipId, Guid teamId, UpdateChampionshipTeamDto dto, CancellationToken ct);

    Task<Result> StartAsync(Guid groupId, Guid championshipId, CancellationToken ct);
    Task<Result<ChampionshipGameCreatedDto>> CreateGameAsync(Guid groupId, Guid championshipId, CreateChampionshipGameDto dto, CancellationToken ct);
    Task<Result> AddGoalAsync(Guid groupId, Guid championshipId, Guid gameId, ChampionshipGoalDto dto, CancellationToken ct);
    Task<Result> RemoveGoalAsync(Guid groupId, Guid championshipId, Guid gameId, Guid goalId, CancellationToken ct);
    Task<Result> SetTeamGoalkeeperAsync(Guid groupId, Guid championshipId, SetGameGoalkeeperDto dto, CancellationToken ct);
    Task<Result> EndGameAsync(Guid groupId, Guid championshipId, Guid gameId, EndChampionshipGameDto dto, CancellationToken ct);
    Task<Result> FinishAsync(Guid groupId, Guid championshipId, CancellationToken ct);
    Task<Result> StepBackAsync(Guid groupId, Guid championshipId, CancellationToken ct);
    Task<Result<ChampionshipGameEventCreatedDto>> PublishGameEventAsync(
        Guid groupId, Guid championshipId, Guid gameId, Guid? userId, ChampionshipGameEventRequestDto dto, CancellationToken ct);

    Task<Result> VoteAsync(Guid groupId, Guid championshipId, Guid userId, ChampionshipVoteDto dto, CancellationToken ct);
    Task<Result> AdminVoteAsync(Guid groupId, Guid championshipId, ChampionshipAdminVoteDto dto, CancellationToken ct);
    Task<Result<ChampionshipVotesDto>> GetVotesAsync(Guid groupId, Guid championshipId, CancellationToken ct);
    Task<Result> FinalizeAsync(Guid groupId, Guid championshipId, FinalizeChampionshipDto dto, CancellationToken ct);
}
