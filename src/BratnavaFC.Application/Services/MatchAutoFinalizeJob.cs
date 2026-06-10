using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class MatchAutoFinalizeJob : IMatchAutoFinalizeJob
{
    private readonly AppDbContext _db;
    private readonly IMatchService _matchService;
    private readonly ILogger<MatchAutoFinalizeJob> _logger;

    public MatchAutoFinalizeJob(AppDbContext db, IMatchService matchService, ILogger<MatchAutoFinalizeJob> logger)
    {
        _db           = db;
        _matchService = matchService;
        _logger       = logger;
    }

    public async Task ExecuteAsync(Guid matchId, Guid groupId, CancellationToken ct = default)
    {
        var status = await _db.Matches
            .AsNoTracking()
            .Where(m => m.Id == matchId && m.GroupId == groupId)
            .Select(m => (MatchStatus?)m.Status)
            .FirstOrDefaultAsync(ct);

        if (status is null)
        {
            _logger.LogInformation("MatchAutoFinalizeJob: partida {MatchId} não encontrada — ignorando.", matchId);
            return;
        }

        if (status != MatchStatus.PostGame)
        {
            _logger.LogInformation(
                "MatchAutoFinalizeJob: partida {MatchId} não está em PostGame (status={Status}) — ignorando.",
                matchId, status);
            return;
        }

        var result = await _matchService.FinalizeMatchAsync(groupId, matchId, ct);

        if (result.Success)
            _logger.LogInformation("MatchAutoFinalizeJob: partida {MatchId} finalizada automaticamente.", matchId);
        else
            _logger.LogWarning("MatchAutoFinalizeJob: falha ao finalizar partida {MatchId} — {Error}.", matchId, result.Error);
    }
}
