using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
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

        return Result<AbsenceDto>.Ok(MapToDto(absence), "Ausência cadastrada com sucesso.", ResultStatus.Created);
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

        return Result<AbsenceDto>.Ok(MapToDto(absence), "Ausência atualizada com sucesso.");
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid absenceId, CancellationToken ct = default)
    {
        var absence = await _context.UserAbsences
            .FirstOrDefaultAsync(a => a.Id == absenceId && a.UserId == userId, ct);

        if (absence is null)
            return Result.Ok("Ausência removida com sucesso.");

        _context.UserAbsences.Remove(absence);
        await _context.SaveChangesAsync(ct);

        return Result.Ok("Ausência removida com sucesso.");
    }

    public async Task<Result<List<GroupMemberAbsenceDto>>> GetByGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        var groupExists = await _context.Groups.AnyAsync(g => g.Id == groupId, ct);
        if (!groupExists)
            return Result<List<GroupMemberAbsenceDto>>.Fail("Grupo não encontrado.", ResultStatus.NotFound);

        var players = await _context.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId && !p.IsGuest && p.UserId != null && p.Status == Status.Active)
            .Select(p => new { p.Id, p.Name, p.UserId })
            .ToListAsync(ct);

        if (players.Count == 0)
            return Result<List<GroupMemberAbsenceDto>>.Ok([]);

        var userIds = players.Select(p => p.UserId!.Value).ToList();

        var absences = await _context.UserAbsences
            .AsNoTracking()
            .Where(a => userIds.Contains(a.UserId))
            .OrderBy(a => a.StartDate)
            .ToListAsync(ct);

        var absencesByUser = absences
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.Select(MapToDto).ToList());

        var result = players
            .Select(p => new GroupMemberAbsenceDto
            {
                PlayerId   = p.Id,
                PlayerName = p.Name,
                Absences   = absencesByUser.TryGetValue(p.UserId!.Value, out var list) ? list : [],
            })
            .OrderBy(m => m.PlayerName)
            .ToList();

        return Result<List<GroupMemberAbsenceDto>>.Ok(result);
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
