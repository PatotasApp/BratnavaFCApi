using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Payments;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class PaymentService : IPaymentService
{
    private readonly AppDbContext _context;

    public PaymentService(AppDbContext context)
    {
        _context = context;
    }

    // ── Grade mensal ──────────────────────────────────────────────────────────

    public async Task<Result<MonthlyGridDto>> GetMonthlyGridAsync(Guid groupId, int year, CancellationToken ct = default)
    {
        // Mensalistas = jogadores ativos, não-guest, com UserId
        var players = await _context.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId
                     && !p.IsGuest
                     && p.UserId != null
                     && p.Status == Status.Active)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.UserId, JoinDate = p.JoinedAt ?? p.CreateDate })
            .ToListAsync(ct);

        if (players.Count == 0)
            return Result<MonthlyGridDto>.Ok(new MonthlyGridDto { Year = year });

        var playerIds = players.Select(p => p.Id).ToList();

        // Busca os registros existentes de pagamento para o ano
        var records = await _context.MonthlyPayments
            .AsNoTracking()
            .Where(m => m.GroupId == groupId
                     && playerIds.Contains(m.PlayerId)
                     && m.Year == year)
            .ToListAsync(ct);

        var settings = await _context.GroupSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.GroupId == groupId, ct);

        var recordMap  = records.ToLookup(r => (r.PlayerId, r.Month));
        var monthlyFee = settings?.MonthlyFee;

        // Só exibe meses até o atual (ano corrente) — meses futuros não são pendências
        var today    = DateTime.UtcNow;
        var maxMonth = year == today.Year ? today.Month : 12;

        var rows = players.Select(p =>
        {
            // Mês mínimo: jogadores que entraram neste ano só devem ser cobrados
            // a partir do mês de entrada — meses anteriores não são pendências.
            var joinYear  = p.JoinDate.Year;
            var joinMonth = p.JoinDate.Month;
            var firstMonth = joinYear == year ? joinMonth
                           : joinYear >  year ? maxMonth + 1  // ainda não existia
                           : 1;                               // já era membro no ano todo

            var count  = Math.Max(0, maxMonth - firstMonth + 1);
            var months = Enumerable.Range(firstMonth, count).Select(m =>
            {
                var rec = recordMap[(p.Id, m)].FirstOrDefault();
                return rec is null
                    ? new MonthlyPaymentCellDto
                    {
                        Month  = m,
                        Status = PaymentStatus.Pending,
                        Amount = monthlyFee ?? 0,
                    }
                    : new MonthlyPaymentCellDto
                    {
                        Month          = m,
                        Status         = rec.Status,
                        Amount         = rec.Amount,
                        Discount       = rec.Discount,
                        DiscountReason = rec.DiscountReason,
                        PaidAt         = rec.PaidAt,
                        HasProof       = rec.ProofBase64 is not null,
                        ProofFileName  = rec.ProofFileName,
                    };
            }).ToArray();

            return new PlayerMonthlyRowDto
            {
                PlayerId   = p.Id,
                UserId     = p.UserId,
                PlayerName = p.Name,
                JoinedYear = joinYear,
                JoinedMonth= joinMonth,
                Months     = months,
            };
        }).ToArray();

        return Result<MonthlyGridDto>.Ok(new MonthlyGridDto
        {
            Year       = year,
            MonthlyFee = monthlyFee,
            Players    = rows,
        });
    }

    // ── Lançamento mensal em lote ──────────────────────────────────────────────

    public async Task<Result<(int Created, int Skipped)>> InitiateMonthlyAsync(
        Guid groupId, int year, int month, CancellationToken ct = default)
    {
        if (month < 1 || month > 12)
            return Result<(int Created, int Skipped)>.Fail("Mês deve estar entre 1 e 12.", ResultStatus.BadRequest);

        var settings = await _context.GroupSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.GroupId == groupId, ct);

        var fee = settings?.MonthlyFee ?? 0m;

        // Mensalistas = ativos, não-guest, com UserId
        var playerIds = await _context.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId
                     && !p.IsGuest
                     && p.UserId != null
                     && p.Status == Status.Active)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (playerIds.Count == 0) return Result<(int Created, int Skipped)>.Ok((0, 0));

        // Quais já têm registro?
        var existing = await _context.MonthlyPayments
            .AsNoTracking()
            .Where(m => m.GroupId == groupId
                     && m.Year == year
                     && m.Month == month
                     && playerIds.Contains(m.PlayerId))
            .Select(m => m.PlayerId)
            .ToListAsync(ct);

        var existingSet = existing.ToHashSet();
        var toCreate = playerIds.Where(id => !existingSet.Contains(id)).ToList();

        if (toCreate.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var playerId in toCreate)
            {
                var record = new MonthlyPaymentEntity(
                    groupId, playerId, year, month, fee);
                await _context.MonthlyPayments.AddAsync(record, ct);
            }
            await _context.SaveChangesAsync(ct);
        }

        return Result<(int Created, int Skipped)>.Ok((toCreate.Count, existingSet.Count));
    }

    public async Task<Result<bool>> IsMonthInitiatedAsync(
        Guid groupId, int year, int month, CancellationToken ct = default)
    {
        var initiated = await _context.MonthlyPayments
            .AsNoTracking()
            .AnyAsync(m => m.GroupId == groupId
                        && m.Year == year
                        && m.Month == month, ct);

        return Result<bool>.Ok(initiated);
    }

    public async Task<Result> UpsertMonthlyPaymentAsync(
        Guid groupId,
        UpsertMonthlyPaymentDto dto,
        Guid actingUserId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        // Jogador não-admin só pode atualizar o próprio pagamento
        if (!isAdmin)
        {
            var ownsPlayer = await _context.Players
                .AnyAsync(p => p.Id == dto.PlayerId
                            && p.GroupId == groupId
                            && p.UserId == actingUserId, ct);
            if (!ownsPlayer)
                return Result.Fail("Sem permissão para esta operação.", ResultStatus.Forbidden);

            // Jogador não pode aplicar desconto
            if (dto.Discount.HasValue)
                return Result.Fail("Sem permissão para esta operação.", ResultStatus.Forbidden);
        }

        var settings = await _context.GroupSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.GroupId == groupId, ct);

        var record = await _context.MonthlyPayments
            .FirstOrDefaultAsync(m => m.GroupId  == groupId
                                   && m.PlayerId == dto.PlayerId
                                   && m.Year     == dto.Year
                                   && m.Month    == dto.Month, ct);

        if (record is null)
        {
            record = new MonthlyPaymentEntity(
                groupId,
                dto.PlayerId,
                dto.Year,
                dto.Month,
                settings?.MonthlyFee ?? 0);

            await _context.MonthlyPayments.AddAsync(record, ct);
        }

        if (dto.Discount.HasValue && isAdmin)
        {
            record.ApplyDiscount(dto.Discount.Value, dto.DiscountReason, actingUserId);
        }

        if (dto.Status == PaymentStatus.Paid)
        {
            record.MarkAsPaid(
                isAdmin ? actingUserId : null,
                dto.ProofBase64,
                dto.ProofFileName,
                dto.ProofMimeType);
        }
        else
        {
            record.MarkAsPending();
        }

        await _context.SaveChangesAsync(ct);

        return Result.Ok("Pagamento atualizado com sucesso.");
    }

    // ── Cobranças extras ──────────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<ExtraChargeDto>>> GetExtraChargesAsync(Guid groupId, CancellationToken ct = default)
    {
        var charges = await _context.ExtraCharges
            .AsNoTracking()
            .Where(c => c.GroupId == groupId)
            .Include(c => c.Payments)
            .OrderByDescending(c => c.CreateDate)
            .ToListAsync(ct);

        // Busca nomes dos jogadores envolvidos
        var playerIds = charges
            .SelectMany(c => c.Payments.Select(p => p.PlayerId))
            .Distinct()
            .ToList();

        var playerNames = playerIds.Count > 0
            ? await _context.Players
                .AsNoTracking()
                .Where(p => playerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, ct)
            : new Dictionary<Guid, string>();

        IReadOnlyList<ExtraChargeDto> result = charges.Select(c => ToExtraChargeDto(c, playerNames)).ToList();
        return Result<IReadOnlyList<ExtraChargeDto>>.Ok(result);
    }

    public async Task<Result<ExtraChargeDto>> CreateExtraChargeAsync(
        Guid groupId,
        CreateExtraChargeDto dto,
        Guid adminId,
        CancellationToken ct = default)
    {
        if (dto.PlayerIds is null || dto.PlayerIds.Length == 0)
            return Result<ExtraChargeDto>.Fail("Selecione ao menos um jogador.", ResultStatus.BadRequest);

        // Valida que os jogadores pertencem à patota
        var validPlayers = await _context.Players
            .AsNoTracking()
            .Where(p => dto.PlayerIds.Contains(p.Id) && p.GroupId == groupId)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(ct);

        if (validPlayers.Count == 0)
            return Result<ExtraChargeDto>.Fail("Nenhum jogador válido encontrado.", ResultStatus.BadRequest);

        var charge = new ExtraChargeEntity(
            groupId,
            dto.Name,
            dto.Description,
            dto.Amount,
            dto.DueDate,
            adminId);

        await _context.ExtraCharges.AddAsync(charge, ct);

        var payments = validPlayers.Select(p =>
            new ExtraChargePaymentEntity(charge.Id, p.Id, groupId, dto.Amount)).ToList();

        await _context.ExtraChargePayments.AddRangeAsync(payments, ct);
        await _context.SaveChangesAsync(ct);

        // Recarrega com navs para retornar DTO completo
        var playerNames = validPlayers.ToDictionary(p => p.Id, p => p.Name);

        var saved = await _context.ExtraCharges
            .AsNoTracking()
            .Include(c => c.Payments)
            .FirstAsync(c => c.Id == charge.Id, ct);

        return Result<ExtraChargeDto>.Ok(ToExtraChargeDto(saved, playerNames), "Cobrança extra criada com sucesso.", ResultStatus.Created);
    }

    public async Task<Result> CancelExtraChargeAsync(Guid groupId, Guid chargeId, CancellationToken ct = default)
    {
        var charge = await _context.ExtraCharges
            .FirstOrDefaultAsync(c => c.Id == chargeId && c.GroupId == groupId, ct);

        if (charge is null)
            return Result.Fail("Cobrança extra não encontrada.", ResultStatus.NotFound);

        charge.Cancel();
        await _context.SaveChangesAsync(ct);

        return Result.Ok("Cobrança extra removida com sucesso.");
    }

    public async Task<Result> BulkDiscountExtraChargeAsync(
        Guid groupId, Guid chargeId, BulkExtraChargeDiscountDto dto, Guid adminId, CancellationToken ct = default)
    {
        if (dto.Discount < 0)
            return Result.Fail("Desconto não pode ser negativo.", ResultStatus.BadRequest);

        if (dto.PlayerIds.Length == 0) return Result.Ok();

        var payments = await _context.ExtraChargePayments
            .Where(p => p.ExtraChargeId == chargeId
                     && p.GroupId       == groupId
                     && dto.PlayerIds.Contains(p.PlayerId))
            .ToListAsync(ct);

        foreach (var payment in payments)
            payment.ApplyDiscount(dto.Discount, dto.DiscountReason, adminId);

        await _context.SaveChangesAsync(ct);

        return Result.Ok("Desconto aplicado com sucesso.");
    }

    public async Task<Result> UpsertExtraChargePaymentAsync(
        Guid groupId,
        Guid chargeId,
        Guid playerId,
        UpsertExtraChargePaymentDto dto,
        Guid actingUserId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        // Jogador só pode atualizar o próprio
        if (!isAdmin)
        {
            var ownsPlayer = await _context.Players
                .AnyAsync(p => p.Id == playerId
                            && p.GroupId == groupId
                            && p.UserId == actingUserId, ct);
            if (!ownsPlayer)
                return Result.Fail("Sem permissão para esta operação.", ResultStatus.Forbidden);

            if (dto.Discount.HasValue)
                return Result.Fail("Sem permissão para esta operação.", ResultStatus.Forbidden);
        }

        var payment = await _context.ExtraChargePayments
            .FirstOrDefaultAsync(p => p.ExtraChargeId == chargeId
                                   && p.PlayerId     == playerId
                                   && p.GroupId      == groupId, ct);

        if (payment is null)
            return Result.Fail("Pagamento não encontrado.", ResultStatus.NotFound);

        if (dto.Discount.HasValue && isAdmin)
            payment.ApplyDiscount(dto.Discount.Value, dto.DiscountReason, actingUserId);

        if (dto.Status == PaymentStatus.Paid)
        {
            payment.MarkAsPaid(
                isAdmin ? actingUserId : null,
                dto.ProofBase64,
                dto.ProofFileName,
                dto.ProofMimeType);
        }
        else
        {
            payment.MarkAsPending();
        }

        await _context.SaveChangesAsync(ct);

        return Result.Ok("Pagamento atualizado com sucesso.");
    }

    // ── Visão do próprio usuário ──────────────────────────────────────────────

    public async Task<Result<PlayerMonthlyRowDto?>> GetMyMonthlyRowAsync(
        Guid groupId, Guid userId, int year, CancellationToken ct = default)
    {
        var player = await _context.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId
                     && p.UserId  == userId
                     && !p.IsGuest
                     && p.Status  == Status.Active)
            .Select(p => new { p.Id, p.Name, p.UserId, JoinDate = p.JoinedAt ?? p.CreateDate })
            .FirstOrDefaultAsync(ct);

        if (player is null) return Result<PlayerMonthlyRowDto?>.Ok(null);

        var settings = await _context.GroupSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.GroupId == groupId, ct);

        var records = await _context.MonthlyPayments
            .AsNoTracking()
            .Where(m => m.GroupId  == groupId
                     && m.PlayerId == player.Id
                     && m.Year     == year)
            .ToListAsync(ct);

        var recordMap  = records.ToLookup(r => r.Month);
        var monthlyFee = settings?.MonthlyFee;

        // Só retorna meses até o mês atual (para o ano corrente)
        // — meses futuros não existem como pendências
        var today    = DateTime.UtcNow;
        var maxMonth = year == today.Year ? today.Month : 12;

        // Mês mínimo: jogador só é cobrado a partir do mês em que entrou na patota
        var joinYear   = player.JoinDate.Year;
        var joinMonth  = player.JoinDate.Month;
        var firstMonth = joinYear == year ? joinMonth
                       : joinYear >  year ? maxMonth + 1
                       : 1;

        var count  = Math.Max(0, maxMonth - firstMonth + 1);
        var months = Enumerable.Range(firstMonth, count).Select(m =>
        {
            var rec = recordMap[m].FirstOrDefault();
            return rec is null
                ? new MonthlyPaymentCellDto { Month = m, Status = PaymentStatus.Pending, Amount = monthlyFee ?? 0 }
                : new MonthlyPaymentCellDto
                {
                    Month          = m,
                    Status         = rec.Status,
                    Amount         = rec.Amount,
                    Discount       = rec.Discount,
                    DiscountReason = rec.DiscountReason,
                    PaidAt         = rec.PaidAt,
                    HasProof       = rec.ProofBase64 is not null,
                    ProofFileName  = rec.ProofFileName,
                };
        }).ToArray();

        return Result<PlayerMonthlyRowDto?>.Ok(new PlayerMonthlyRowDto
        {
            PlayerId    = player.Id,
            UserId      = player.UserId,
            PlayerName  = player.Name,
            JoinedYear  = joinYear,
            JoinedMonth = joinMonth,
            Months      = months,
        });
    }

    public async Task<Result<IReadOnlyList<ExtraChargeDto>>> GetMyExtraChargesAsync(
        Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var playerId = await _context.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId
                     && p.UserId  == userId
                     && !p.IsGuest)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        if (playerId is null) return Result<IReadOnlyList<ExtraChargeDto>>.Ok([]);

        var payments = await _context.ExtraChargePayments
            .AsNoTracking()
            .Where(ep => ep.GroupId  == groupId
                      && ep.PlayerId == playerId.Value)
            .Include(ep => ep.ExtraCharge)
            .ToListAsync(ct);

        if (payments.Count == 0) return Result<IReadOnlyList<ExtraChargeDto>>.Ok([]);

        var playerName = await _context.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId.Value)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct) ?? "?";

        var names = new Dictionary<Guid, string> { [playerId.Value] = playerName };

        IReadOnlyList<ExtraChargeDto> result = payments
            .Where(ep => ep.ExtraCharge is not null)
            .GroupBy(ep => ep.ExtraChargeId)
            .Select(g =>
            {
                var charge = g.First().ExtraCharge!;
                return new ExtraChargeDto
                {
                    Id          = charge.Id,
                    Name        = charge.Name,
                    Description = charge.Description,
                    Amount      = charge.Amount,
                    DueDate     = charge.DueDate,
                    CreatedAt   = charge.CreateDate,
                    IsCancelled = charge.IsCancelled,
                    Payments    = g.Select(ep => new ExtraChargePaymentDto
                    {
                        PlayerId       = ep.PlayerId,
                        PlayerName     = names.GetValueOrDefault(ep.PlayerId, "?"),
                        Amount         = ep.Amount,
                        Discount       = ep.Discount,
                        FinalAmount    = ep.FinalAmount,
                        DiscountReason = ep.DiscountReason,
                        Status         = ep.Status,
                        PaidAt         = ep.PaidAt,
                        HasProof       = ep.ProofBase64 is not null,
                        ProofFileName  = ep.ProofFileName,
                    }).ToArray(),
                };
            })
            .OrderByDescending(c => c.CreatedAt)
            .ToList();

        return Result<IReadOnlyList<ExtraChargeDto>>.Ok(result);
    }

    // ── Resumo ────────────────────────────────────────────────────────────────

    public async Task<Result<PaymentSummaryDto>> GetMySummaryAsync(
        Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var player = await _context.Players
            .Where(p => p.GroupId == groupId && p.UserId == userId && !p.IsGuest)
            .Select(p => new { p.Id })
            .FirstOrDefaultAsync(ct);

        if (player is null)
            return Result<PaymentSummaryDto>.Ok(new PaymentSummaryDto()); // sem player na patota → sem pendências

        return await GetPaymentSummaryAsync(groupId, player.Id, ct: ct);
    }

    public async Task<Result<PaymentSummaryDto>> GetPaymentSummaryAsync(
        Guid groupId,
        Guid playerId,
        Guid? requestingUserId = null,
        bool isAdmin = true,
        CancellationToken ct = default)
    {
        if (!isAdmin && requestingUserId.HasValue)
        {
            var owns = await _context.Players.AnyAsync(
                p => p.Id == playerId && p.GroupId == groupId && p.UserId == requestingUserId.Value, ct);
            if (!owns) return Result<PaymentSummaryDto>.Fail("Acesso negado.", ResultStatus.Forbidden);
        }
        var year = DateTime.UtcNow.Year;

        var settings = await _context.GroupSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.GroupId == groupId, ct);

        // Mensalidade só é contabilizada se a patota tiver fee configurado
        int pendingMonths = 0;
        if (settings?.MonthlyFee > 0)
        {
            var paidMonths = await _context.MonthlyPayments
                .AsNoTracking()
                .Where(m => m.GroupId  == groupId
                         && m.PlayerId == playerId
                         && m.Year     == year
                         && m.Status   == PaymentStatus.Paid)
                .Select(m => m.Month)
                .ToListAsync(ct);

            var currentMonth = DateTime.UtcNow.Month;
            pendingMonths = Enumerable.Range(1, currentMonth)
                .Count(m => !paidMonths.Contains(m));
        }

        // Cobranças extras pendentes (não canceladas)
        var pendingExtras = await _context.ExtraChargePayments
            .AsNoTracking()
            .Include(ep => ep.ExtraCharge)
            .Where(ep => ep.GroupId  == groupId
                      && ep.PlayerId == playerId
                      && ep.Status   == PaymentStatus.Pending
                      && !ep.ExtraCharge!.IsCancelled)
            .Select(ep => new ExtraChargePendingDto
            {
                ChargeId    = ep.ExtraChargeId,
                ChargeName  = ep.ExtraCharge!.Name,
                Amount      = ep.Amount,
                Discount    = ep.Discount,
                FinalAmount = Math.Max(0, ep.Amount - ep.Discount),
                DueDate     = ep.ExtraCharge.DueDate,
            })
            .ToListAsync(ct);

        return Result<PaymentSummaryDto>.Ok(new PaymentSummaryDto
        {
            HasPendingMonthly  = pendingMonths > 0,
            PendingMonthsCount = pendingMonths,
            PendingExtraCharges = pendingExtras.ToArray(),
        });
    }

    // ── Comprovantes ──────────────────────────────────────────────────────────

    public async Task<Result<ProofResponseDto>> GetMonthlyProofAsync(
        Guid groupId, Guid playerId, int year, int month,
        Guid? requestingUserId = null, bool isAdmin = true,
        CancellationToken ct = default)
    {
        if (!isAdmin && requestingUserId.HasValue)
        {
            var owns = await _context.Players.AnyAsync(
                p => p.Id == playerId && p.GroupId == groupId && p.UserId == requestingUserId.Value, ct);
            if (!owns) return Result<ProofResponseDto>.Fail("Acesso negado.", ResultStatus.Forbidden);
        }
        var record = await _context.MonthlyPayments
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.GroupId  == groupId
                                   && m.PlayerId == playerId
                                   && m.Year     == year
                                   && m.Month    == month, ct);

        if (record?.ProofBase64 is null)
            return Result<ProofResponseDto>.Fail("Comprovante não encontrado.", ResultStatus.NotFound);

        return Result<ProofResponseDto>.Ok(new ProofResponseDto
        {
            Base64   = record.ProofBase64,
            FileName = record.ProofFileName ?? "comprovante",
            MimeType = record.ProofMimeType ?? "application/octet-stream",
        });
    }

    public async Task<Result<ProofResponseDto>> GetExtraChargeProofAsync(
        Guid groupId, Guid chargeId, Guid playerId,
        Guid? requestingUserId = null, bool isAdmin = true,
        CancellationToken ct = default)
    {
        if (!isAdmin && requestingUserId.HasValue)
        {
            var owns = await _context.Players.AnyAsync(
                p => p.Id == playerId && p.GroupId == groupId && p.UserId == requestingUserId.Value, ct);
            if (!owns) return Result<ProofResponseDto>.Fail("Acesso negado.", ResultStatus.Forbidden);
        }
        var record = await _context.ExtraChargePayments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.GroupId      == groupId
                                   && p.ExtraChargeId == chargeId
                                   && p.PlayerId      == playerId, ct);

        if (record?.ProofBase64 is null)
            return Result<ProofResponseDto>.Fail("Comprovante não encontrado.", ResultStatus.NotFound);

        return Result<ProofResponseDto>.Ok(new ProofResponseDto
        {
            Base64   = record.ProofBase64,
            FileName = record.ProofFileName ?? "comprovante",
            MimeType = record.ProofMimeType ?? "application/octet-stream",
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ExtraChargeDto ToExtraChargeDto(ExtraChargeEntity c, Dictionary<Guid, string> names) => new()
    {
        Id          = c.Id,
        Name        = c.Name,
        Description = c.Description,
        Amount      = c.Amount,
        DueDate     = c.DueDate,
        CreatedAt   = c.CreateDate,
        IsCancelled = c.IsCancelled,
        Payments    = c.Payments.Select(p => new ExtraChargePaymentDto
        {
            PlayerId       = p.PlayerId,
            PlayerName     = names.GetValueOrDefault(p.PlayerId, "?"),
            Amount         = p.Amount,
            Discount       = p.Discount,
            FinalAmount    = p.FinalAmount,
            DiscountReason = p.DiscountReason,
            Status         = p.Status,
            PaidAt         = p.PaidAt,
            HasProof       = p.ProofBase64 is not null,
            ProofFileName  = p.ProofFileName,
        }).ToArray(),
    };
}
