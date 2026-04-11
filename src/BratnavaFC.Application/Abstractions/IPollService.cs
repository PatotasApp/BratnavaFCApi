using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Polls;

namespace BratnavaFC.Application.Abstractions;

public interface IPollService
{
    Task<Result<List<PollSummaryDto>>> GetPollsAsync(Guid groupId, Guid playerId, CancellationToken ct = default);
    Task<Result<PollDto>> GetPollAsync(Guid groupId, Guid pollId, Guid playerId, bool isAdmin, CancellationToken ct = default, bool skipImages = false);
    Task<Result<PollDto>> CreatePollAsync(Guid groupId, Guid userId, CreatePollDto dto, CancellationToken ct = default);
    Task<Result<PollDto>> CreateEventPollAsync(Guid groupId, Guid userId, CreateEventPollDto dto, CancellationToken ct = default);
    Task<Result> ClosePollAsync(Guid groupId, Guid pollId, Guid userId, ClosePollDto dto, CancellationToken ct = default);
    Task<Result> ReopenPollAsync(Guid groupId, Guid pollId, CancellationToken ct = default);
    Task<Result> SetShowVotesAsync(Guid groupId, Guid pollId, bool showVotes, CancellationToken ct = default);
    Task<Result> DeletePollAsync(Guid groupId, Guid pollId, CancellationToken ct = default);
    Task<Result<PollOptionDto>> AddOptionAsync(Guid groupId, Guid pollId, AddPollOptionDto dto, CancellationToken ct = default);
    Task<Result<PollOptionDto>> UpdateOptionAsync(Guid groupId, Guid pollId, Guid optionId, UpdatePollOptionDto dto, CancellationToken ct = default);
    Task<Result> DeleteOptionAsync(Guid groupId, Guid pollId, Guid optionId, CancellationToken ct = default);
    Task<Result<PollDto>> CastVoteAsync(Guid groupId, Guid pollId, Guid playerId, CastVoteDto dto, bool isAdmin = false, CancellationToken ct = default);
    Task<Result<PollDto>> RemoveVoteAsync(Guid groupId, Guid pollId, Guid playerId, bool isAdmin = false, CancellationToken ct = default);
    Task<Result<PollDto>> AdminCastVoteAsync(Guid groupId, Guid pollId, AdminCastVoteDto dto, CancellationToken ct = default);
}
