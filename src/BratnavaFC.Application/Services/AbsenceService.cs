using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Absences;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class AbsenceService : IAbsenceService
{
    private readonly AppDbContext _context;

    public AbsenceService(AppDbContext context) => _context = context;

    public async Task<Result<List<AbsenceDto>>> GetMineAsync(Guid userId, CancellationToken ct = default)
    {
        var absences = await _context.UserAbsences
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.StartDate)
            .ToListAsync(ct);

        return Result<List<AbsenceDto>>.Ok(absences.Select(MapToDto).ToList());
    }

    public async Task<Result<AbsenceDto>> CreateAsync(Guid userId, CreateAbsenceDto dto, CancellationToken ct = default)
    {
        if (dto.EndDate < dto.StartDate)
            return Result<AbsenceDto>.Fail("EndDate não pode ser anterior a StartDate.", ResultStatus.BadRequest);

        if (!Enum.IsDefined(typeof(AbsenceType), dto.AbsenceType))
            return Result<AbsenceDto>.Fail("Tipo de ausência inválido.", ResultStatus.BadRequest);

        var absence = new UserAbsenceEntity(userId, dto.StartDate, dto.EndDate, dto.AbsenceType, dto.Description);
        _context.UserAbsences.Add(absence);
        await _context.SaveChangesAsync(ct);

        var affected = await ApplyAbsenceToUpcomingMatchesAsync(absence, ct);

        var msg = affected > 0
            ? $"Ausência cadastrada. Presença recusada em {affected} partida{(affected != 1 ? "s" : "")} do período."
            : "Ausência cadastrada com sucesso.";
        return Result<AbsenceDto>.Ok(MapToDto(absence), msg, ResultStatus.Created);
    }

    public async Task<Result<AbsenceDto>> UpdateAsync(Guid userId, Guid absenceId, CreateAbsenceDto dto, CancellationToken ct = default)
    {
        if (dto.EndDate < dto.StartDate)
            return Result<AbsenceDto>.Fail("EndDate não pode ser anterior a StartDate.", ResultStatus.BadRequest);

        if (!Enum.IsDefined(typeof(AbsenceType), dto.AbsenceType))
            return Result<AbsenceDto>.Fail("Tipo de ausência inválido.", ResultStatus.BadRequest);

        var absence = await _context.UserAbsences
            .FirstOrDefaultAsync(a => a.Id == absenceId && a.UserId == userId, ct);

        if (absence is null)
            return Result<AbsenceDto>.Fail("Ausência não encontrada.", ResultStatus.NotFound);

        absence.Update(dto.StartDate, dto.EndDate, dto.AbsenceType, dto.Description);
        await _context.SaveChangesAsync(ct);

        // Reaplica nas partidas: reverte rejeições fora do novo período e aplica nas que passaram a fazer parte
        await RevertAbsenceFromUpcomingMatchesAsync(absence.Id, exceptRange: (absence.StartDate, absence.EndDate), ct);
        await ApplyAbsenceToUpcomingMatchesAsync(absence, ct);

        return Result<AbsenceDto>.Ok(MapToDto(absence), "Ausência atualizada com sucesso.");
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid absenceId, CancellationToken ct = default)
    {
        var absence = await _context.UserAbsences
            .FirstOrDefaultAsync(a => a.Id == absenceId && a.UserId == userId, ct);

        if (absence is null)
            return Result.Ok("Ausência removida com sucesso.");

        // Volta para pendente os convites que foram recusados automaticamente por esta ausência
        await RevertAbsenceFromUpcomingMatchesAsync(absence.Id, exceptRange: null, ct);

        _context.UserAbsences.Remove(absence);
        await _context.SaveChangesAsync(ct);

        return Result.Ok("Ausência removida com sucesso.");
    }

    public async Task<Result<PagedResultDto<GroupAbsenceItemDto>>> GetByGroupAsync(
        Guid groupId, string? status = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var groupExists = await _context.Groups.AnyAsync(g => g.Id == groupId, ct);
        if (!groupExists)
            return Result<PagedResultDto<GroupAbsenceItemDto>>.Fail("Grupo não encontrado.", ResultStatus.NotFound);

        (page, pageSize) = Pagination.Normalize(page, pageSize);

        var players = await _context.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId && !p.IsGuest && p.UserId != null && p.Status == Status.Active)
            .Select(p => new { p.Id, p.Name, p.UserId })
            .ToListAsync(ct);

        var empty = new PagedResultDto<GroupAbsenceItemDto> { Page = page, PageSize = pageSize, Total = 0, Items = [] };
        if (players.Count == 0)
            return Result<PagedResultDto<GroupAbsenceItemDto>>.Ok(empty);

        var userIds = players.Select(p => p.UserId!.Value).ToList();
        var today   = DateOnly.FromDateTime(DateTime.UtcNow);

        var query = _context.UserAbsences
            .AsNoTracking()
            .Where(a => userIds.Contains(a.UserId));

        // upcoming = em andamento ou futuras (ordem cronológica); past = encerradas (mais recentes primeiro)
        query = status switch
        {
            "past"     => query.Where(a => a.EndDate < today).OrderByDescending(a => a.StartDate),
            "upcoming" => query.Where(a => a.EndDate >= today).OrderBy(a => a.StartDate).ThenBy(a => a.EndDate),
            _          => query.OrderBy(a => a.StartDate),
        };

        var total = await query.CountAsync(ct);

        var absences = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var playersByUser = players
            .GroupBy(p => p.UserId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var items = absences.Select(a =>
        {
            var player = playersByUser[a.UserId];
            return new GroupAbsenceItemDto
            {
                Id              = a.Id,
                PlayerId        = player.Id,
                PlayerName      = player.Name,
                StartDate       = a.StartDate,
                EndDate         = a.EndDate,
                AbsenceType     = (int)a.AbsenceType,
                AbsenceTypeName = GetTypeName(a.AbsenceType),
                Description     = a.Description,
                CreatedAt       = a.CreateDate,
            };
        }).ToList();

        return Result<PagedResultDto<GroupAbsenceItemDto>>.Ok(new PagedResultDto<GroupAbsenceItemDto>
        {
            Page = page, PageSize = pageSize, Total = total, Items = items,
        });
    }

    // ── Sincronização com partidas ────────────────────────────────────────────

    /// <summary>
    /// Recusa automaticamente a participação do usuário em todas as partidas ainda não
    /// iniciadas cuja data caia dentro do período da ausência. Retorna quantas foram afetadas.
    /// </summary>
    private async Task<int> ApplyAbsenceToUpcomingMatchesAsync(UserAbsenceEntity absence, CancellationToken ct)
    {
        // Limites do período são datas locais (BR); converte para UTC para comparar com PlayedAt (timestamptz).
        var rangeStart = TimeZoneInfo.ConvertTimeToUtc(
            absence.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), BrazilTimeZone.Instance);
        var rangeEnd   = TimeZoneInfo.ConvertTimeToUtc(
            absence.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), BrazilTimeZone.Instance);

        var matchPlayers = await _context.MatchPlayers
            .Include(mp => mp.Match)
            .Where(mp => mp.Player!.UserId == absence.UserId
                      && mp.Match!.PlayedAt >= rangeStart
                      && mp.Match!.PlayedAt <  rangeEnd
                      && mp.Match!.Status   <= MatchStatus.MatchMaking
                      && mp.AutoRejectedByAbsenceId != absence.Id)
            .ToListAsync(ct);

        if (matchPlayers.Count == 0) return 0;

        foreach (var mp in matchPlayers)
            mp.AutoRejectByAbsence(absence.Id);

        await _context.SaveChangesAsync(ct);
        return matchPlayers.Count;
    }

    /// <summary>
    /// Reverte para pendente os convites recusados automaticamente por esta ausência em
    /// partidas ainda não iniciadas. Quando <paramref name="exceptRange"/> é informado,
    /// preserva as rejeições de partidas que continuam dentro do período.
    /// </summary>
    private async Task RevertAbsenceFromUpcomingMatchesAsync(
        Guid absenceId, (DateOnly Start, DateOnly End)? exceptRange, CancellationToken ct)
    {
        var query = _context.MatchPlayers
            .Include(mp => mp.Match)
            .Where(mp => mp.AutoRejectedByAbsenceId == absenceId
                      && mp.Match!.Status <= MatchStatus.MatchMaking);

        if (exceptRange is { } range)
        {
            var keepStart = TimeZoneInfo.ConvertTimeToUtc(
                range.Start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), BrazilTimeZone.Instance);
            var keepEnd   = TimeZoneInfo.ConvertTimeToUtc(
                range.End.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), BrazilTimeZone.Instance);
            query = query.Where(mp => mp.Match!.PlayedAt < keepStart || mp.Match!.PlayedAt >= keepEnd);
        }

        var matchPlayers = await query.ToListAsync(ct);
        if (matchPlayers.Count == 0) return;

        foreach (var mp in matchPlayers)
            mp.ClearAutoRejection();

        await _context.SaveChangesAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AbsenceDto MapToDto(UserAbsenceEntity a) => new()
    {
        Id             = a.Id,
        StartDate      = a.StartDate,
        EndDate        = a.EndDate,
        AbsenceType    = (int)a.AbsenceType,
        AbsenceTypeName = GetTypeName(a.AbsenceType),
        Description    = a.Description,
        CreatedAt      = a.CreateDate,
    };

    internal static string GetTypeName(AbsenceType type) => type switch
    {
        AbsenceType.Travel            => "Viagem",
        AbsenceType.MedicalDepartment => "Departamento Médico",
        AbsenceType.Personal          => "Pessoal",
        AbsenceType.Other             => "Outros",
        _                             => "Outros",
    };
}
