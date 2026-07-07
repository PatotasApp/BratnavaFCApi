using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Payments;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class PaymentService : IPaymentService
{
    private readonly AppDbContext _context;
    private readonly IPushService _push;
    private readonly ILogger<PaymentService> _logger;
    private readonly IFinancialTransactionService _transactions;

    public PaymentService(
        AppDbContext context,
        IPushService push,
        ILogger<PaymentService> logger,
        IFinancialTransactionService transactions)
    {
        _context      = context;
        _push         = push;
        _logger       = logger;
        _transactions = transactions;
    }

    private static readonly string[] _monthNames =
    [
        "Janeiro", "Fevereiro", "Março",    "Abril",
        "Maio",    "Junho",     "Julho",    "Agosto",
        "Setembro","Outubro",   "Novembro", "Dezembro"
    ];

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
            .Select(p => new { p.Id, p.Name, p.UserId, p.IsGoalkeeper, JoinDate = p.JoinedAt ?? p.CreateDate })
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

        var recordMap        = records.ToLookup(r => (r.PlayerId, r.Month));
        var monthlyFee       = settings?.MonthlyFee;
        var goalkeeperFee    = settings?.GoalkeeperMonthlyFee;

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
                var effectiveFee = p.IsGoalkeeper
                    ? (goalkeeperFee ?? monthlyFee ?? 0)
                    : (monthlyFee ?? 0);

                return rec is null
                    ? new MonthlyPaymentCellDto
                    {
                        Month  = m,
                        Status = PaymentStatus.Pending,
                        Amount = effectiveFee,
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
                PlayerId     = p.Id,
                UserId       = p.UserId,
                PlayerName   = p.Name,
                IsGoalkeeper = p.IsGoalkeeper,
                JoinedYear   = joinYear,
                JoinedMonth  = joinMonth,
                Months       = months,
            };
        }).ToArray();

        return Result<MonthlyGridDto>.Ok(new MonthlyGridDto
        {
            Year                 = year,
            MonthlyFee           = monthlyFee,
            GoalkeeperMonthlyFee = goalkeeperFee,
            Players              = rows,
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

        var linePlayerFee  = settings?.MonthlyFee ?? 0m;
        var goalkeeperFee2 = settings?.GoalkeeperMonthlyFee;

        // Mensalistas = ativos, não-guest, com UserId
        var players = await _context.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId
                     && !p.IsGuest
                     && p.UserId != null
                     && p.Status == Status.Active)
            .Select(p => new { p.Id, p.IsGoalkeeper })
            .ToListAsync(ct);

        if (players.Count == 0) return Result<(int Created, int Skipped)>.Ok((0, 0));

        var playerIds = players.Select(p => p.Id).ToList();

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
        var toCreate = players.Where(p => !existingSet.Contains(p.Id)).ToList();

        if (toCreate.Count > 0)
        {
            foreach (var player in toCreate)
            {
                var fee = player.IsGoalkeeper
                    ? (goalkeeperFee2 ?? linePlayerFee)
                    : linePlayerFee;
                var record = new MonthlyPaymentEntity(
                    groupId, player.Id, year, month, fee);
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
            var isGoalkeeper = await _context.Players
                .AsNoTracking()
                .Where(p => p.Id == dto.PlayerId)
                .Select(p => p.IsGoalkeeper)
                .FirstOrDefaultAsync(ct);

            var fee = isGoalkeeper
                ? (settings?.GoalkeeperMonthlyFee ?? settings?.MonthlyFee ?? 0)
                : (settings?.MonthlyFee ?? 0);

            record = new MonthlyPaymentEntity(groupId, dto.PlayerId, dto.Year, dto.Month, fee);
            await _context.MonthlyPayments.AddAsync(record, ct);
        }

        // Captura o effective antes de aplicar o desconto — usado no caixa quando
        // o desconto integral zera o effective mas auto-marca o registro como Paid.
        var monthlyEffectiveBefore = Math.Max(0, record.Amount - record.Discount);

        if (dto.Discount.HasValue && isAdmin)
        {
            record.ApplyDiscount(dto.Discount.Value, dto.DiscountReason, actingUserId);
        }

        var wasAlreadyPending = record.Status == PaymentStatus.Pending;
        var wasAlreadyPaid    = record.Status == PaymentStatus.Paid;

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

        // ── Caixa: registra/remove entrada automática ────────────────────────
        var monthlyPlayerName = await _context.Players
            .AsNoTracking()
            .Where(p => p.Id == record.PlayerId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct);

        var monthlyEffectiveAfter = Math.Max(0, record.Amount - record.Discount);
        var monthlyCaixaAmount    = record.Status == PaymentStatus.Paid && monthlyEffectiveAfter <= 0
            ? monthlyEffectiveBefore
            : monthlyEffectiveAfter;

        await _transactions.RecordOrRemovePaymentEntryAsync(
            groupId,
            TransactionSourceType.MonthlyPayment,
            record.Id,
            monthlyCaixaAmount,
            $"Mensalidade {_monthNames[record.Month - 1]}/{record.Year} – {monthlyPlayerName}",
            new DateOnly(record.Year, record.Month, 1),
            record.Status == PaymentStatus.Paid,
            monthlyPlayerName,
            ct);

        if (dto.Status == PaymentStatus.Paid && !wasAlreadyPaid)
            await NotifyFinanceirosMonthlyPaidAsync(groupId, dto.PlayerId, dto.Month, dto.Year, ct);

        if (dto.Status == PaymentStatus.Pending && !wasAlreadyPending)
            await NotifyPlayerMonthlyPendingAsync(groupId, dto.PlayerId, dto.Month, dto.Year, ct);

        return Result.Ok("Pagamento atualizado com sucesso.");
    }

    // ── Cobranças extras ──────────────────────────────────────────────────────

    public async Task<Result<PagedResultDto<ExtraChargeDto>>> GetExtraChargesAsync(
        Guid groupId, int? year = null, int? month = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        var query = _context.ExtraCharges
            .AsNoTracking()
            .Where(c => c.GroupId == groupId);

        if (year.HasValue)
            query = query.Where(c => c.CreateDate.Year == year.Value);
        if (month.HasValue)
            query = query.Where(c => c.CreateDate.Month == month.Value);

        var total = await query.CountAsync(ct);

        var charges = await query
            .Include(c => c.Payments)
            .OrderByDescending(c => c.CreateDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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

        var items = charges.Select(c => ToExtraChargeDto(c, playerNames)).ToList();
        return Result<PagedResultDto<ExtraChargeDto>>.Ok(new PagedResultDto<ExtraChargeDto>
        {
            Page = page, PageSize = pageSize, Total = total, Items = items,
        });
    }

    public async Task<Result<IReadOnlyList<ExtraChargeMonthSummaryDto>>> GetExtraChargesSummaryAsync(
        Guid groupId, int year, Guid? userId = null, CancellationToken ct = default)
    {
        var query = _context.ExtraCharges
            .AsNoTracking()
            .Where(c => c.GroupId == groupId && c.CreateDate.Year == year);

        // Visão do próprio jogador: considera só cobranças em que ele está incluído
        if (userId.HasValue)
        {
            var playerId = await _context.Players
                .AsNoTracking()
                .Where(p => p.GroupId == groupId && p.UserId == userId.Value && !p.IsGuest)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(ct);

            if (playerId is null)
                return Result<IReadOnlyList<ExtraChargeMonthSummaryDto>>.Ok([]);

            query = query.Where(c => c.Payments.Any(p => p.PlayerId == playerId.Value));
        }

        var months = await query
            .Select(c => new
            {
                Month       = c.CreateDate.Month,
                c.IsCancelled,
                AllPaid     = c.Payments.Count > 0 && c.Payments.All(p => p.Status == PaymentStatus.Paid),
            })
            .ToListAsync(ct);

        IReadOnlyList<ExtraChargeMonthSummaryDto> result = months
            .GroupBy(m => m.Month)
            .Select(g =>
            {
                var active = g.Where(x => !x.IsCancelled).ToList();
                return new ExtraChargeMonthSummaryDto
                {
                    Month      = g.Key,
                    Count      = g.Count(),
                    AllPaid    = active.Count > 0 && active.All(x => x.AllPaid),
                    HasPending = active.Any(x => !x.AllPaid),
                };
            })
            .OrderBy(s => s.Month)
            .ToList();

        return Result<IReadOnlyList<ExtraChargeMonthSummaryDto>>.Ok(result);
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
            .Select(p => new { p.Id, p.Name, p.UserId })
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

        await NotifyPlayersExtraChargeCreatedAsync(validPlayers.Select(p => p.UserId), charge.Name, dto.Amount, groupId, ct);

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

    public async Task<Result<ExtraChargeDto>> ReactivateExtraChargeAsync(Guid groupId, Guid chargeId, CancellationToken ct = default)
    {
        var charge = await _context.ExtraCharges
            .Include(c => c.Payments)
            .FirstOrDefaultAsync(c => c.Id == chargeId && c.GroupId == groupId, ct);

        if (charge is null)
            return Result<ExtraChargeDto>.Fail("Cobrança extra não encontrada.", ResultStatus.NotFound);

        if (!charge.IsCancelled)
            return Result<ExtraChargeDto>.Fail("Cobrança extra não está cancelada.", ResultStatus.BadRequest);

        charge.Reactivate();
        await _context.SaveChangesAsync(ct);

        var playerNames = await _context.Players
            .Where(p => p.GroupId == groupId)
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        return Result<ExtraChargeDto>.Ok(ToExtraChargeDto(charge, playerNames), "Cobrança extra reativada com sucesso.");
    }

    public async Task<Result<ExtraChargeDto>> UpdateExtraChargeDetailsAsync(Guid groupId, Guid chargeId, UpdateExtraChargeDetailsDto dto, CancellationToken ct = default)
    {
        var charge = await _context.ExtraCharges
            .Include(c => c.Payments)
            .FirstOrDefaultAsync(c => c.Id == chargeId && c.GroupId == groupId, ct);

        if (charge is null)
            return Result<ExtraChargeDto>.Fail("Cobrança extra não encontrada.", ResultStatus.NotFound);

        try { charge.UpdateDetails(dto.Name, dto.Description, dto.Amount); }
        catch (InvalidOperationException ex)
        { return Result<ExtraChargeDto>.Fail(ex.Message, ResultStatus.BadRequest); }

        await _context.SaveChangesAsync(ct);

        var playerNames = await _context.Players
            .Where(p => p.GroupId == groupId)
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        return Result<ExtraChargeDto>.Ok(ToExtraChargeDto(charge, playerNames), "Cobrança extra atualizada com sucesso.");
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

        // Captura o valor efetivo ANTES do desconto. Usado no caixa quando o
        // desconto integral zera o effective: o pagamento auto-paga (Paid) mas
        // amount - discount = 0 → usa effectiveBefore para registrar a entrada.
        var effectiveBeforeMap = payments.ToDictionary(
            p => p.Id,
            p => Math.Max(0, p.Amount - p.Discount));

        foreach (var payment in payments)
        {
            payment.ApplyDiscount(dto.Discount, dto.DiscountReason, adminId);

            // Se MarkAsPaid e o pagamento ainda está pendente (desconto parcial),
            // marcar como pago para que a entrada apareça no caixa.
            if (dto.MarkAsPaid && payment.Status == PaymentStatus.Pending)
                payment.MarkAsPaid(adminId, null, null, null);
        }

        await _context.SaveChangesAsync(ct);

        // ── Caixa: atualiza ou cria entradas afetadas ────────────────────────
        if (payments.Count > 0)
        {
            var playerIds = payments.Select(p => p.PlayerId).Distinct().ToList();
            var playerMap = await _context.Players.AsNoTracking()
                .Where(p => playerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

            var chargeName = await _context.ExtraCharges.AsNoTracking()
                .Where(c => c.Id == chargeId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct) ?? "Cobrança";

            foreach (var payment in payments)
            {
                var playerName   = playerMap.GetValueOrDefault(payment.PlayerId, "Jogador");
                var effectiveAfter = Math.Max(0, payment.Amount - payment.Discount);

                // Desconto integral auto-paga o registro (Discount >= Amount) mas zera
                // o effective. Nesse caso usa effectiveBefore como valor no caixa —
                // o admin está confirmando o recebimento do valor que era devido.
                var caixaAmount = payment.Status == PaymentStatus.Paid && effectiveAfter <= 0
                    ? effectiveBeforeMap.GetValueOrDefault(payment.Id)
                    : effectiveAfter;

                await _transactions.RecordOrRemovePaymentEntryAsync(
                    groupId,
                    TransactionSourceType.ExtraCharge,
                    payment.Id,
                    caixaAmount,
                    $"{chargeName} – {playerName}",
                    payment.PaidAt.HasValue
                        ? DateOnly.FromDateTime(payment.PaidAt.Value)
                        : DateOnly.FromDateTime(DateTime.UtcNow),
                    payment.Status == PaymentStatus.Paid,
                    playerName,
                    ct);
            }

            var affectedUserIds = await _context.Players
                .AsNoTracking()
                .Where(p => playerIds.Contains(p.Id) && p.UserId != null)
                .Select(p => p.UserId!.Value)
                .Distinct()
                .ToListAsync(ct);

            if (affectedUserIds.Count > 0)
                _ = NotifyBulkDiscountAsync(groupId, chargeName, dto.Discount, affectedUserIds, ct);
        }

        return Result.Ok("Desconto aplicado com sucesso.");
    }

    private async Task NotifyBulkDiscountAsync(
        Guid groupId, string chargeName, decimal discount,
        List<Guid> userIds, CancellationToken ct)
    {
        try
        {
            var discountStr = discount.ToString("N2");
            await _push.SendToUsersAsync(
                userIds,
                title: "Desconto aplicado! 🎉",
                body:  $"Um desconto de R$ {discountStr} foi aplicado na cobrança \"{chargeName}\".",
                data: new Dictionary<string, string>
                {
                    ["type"]    = "extra_charge_discount",
                    ["groupId"] = groupId.ToString(),
                },
                groupId: groupId);
        }
        catch { /* notificação não crítica */ }
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

        // Captura o effective antes de aplicar o desconto — usado no caixa quando
        // o desconto integral zera o effective mas auto-marca o registro como Paid.
        var extraEffectiveBefore = Math.Max(0, payment.Amount - payment.Discount);

        if (dto.Discount.HasValue && isAdmin)
            payment.ApplyDiscount(dto.Discount.Value, dto.DiscountReason, actingUserId);

        var wasAlreadyPendingExtra = payment.Status == PaymentStatus.Pending;
        var wasAlreadyPaidExtra    = payment.Status == PaymentStatus.Paid;

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

        // ── Caixa: registra/remove entrada automática ────────────────────────
        var extraNames = await _context.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId)
            .Select(p => new { p.Name })
            .FirstOrDefaultAsync(ct);

        var chargeName = await _context.ExtraCharges
            .AsNoTracking()
            .Where(c => c.Id == chargeId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);

        var extraEffectiveAfter = Math.Max(0, payment.Amount - payment.Discount);
        var extraCaixaAmount    = payment.Status == PaymentStatus.Paid && extraEffectiveAfter <= 0
            ? extraEffectiveBefore
            : extraEffectiveAfter;

        await _transactions.RecordOrRemovePaymentEntryAsync(
            groupId,
            TransactionSourceType.ExtraCharge,
            payment.Id,
            extraCaixaAmount,
            $"{chargeName} – {extraNames?.Name}",
            payment.PaidAt.HasValue
                ? DateOnly.FromDateTime(payment.PaidAt.Value)
                : DateOnly.FromDateTime(DateTime.UtcNow),
            payment.Status == PaymentStatus.Paid,
            extraNames?.Name,
            ct);

        if (dto.Status == PaymentStatus.Paid && !wasAlreadyPaidExtra)
        {
            await NotifyFinanceirosExtraChargePaidAsync(groupId, playerId, chargeId, ct);
            await NotifyIfChargeFullyPaidAsync(groupId, chargeId, ct);
        }

        if (dto.Status == PaymentStatus.Pending && !wasAlreadyPendingExtra)
            await NotifyPlayerExtraChargePendingAsync(groupId, playerId, chargeId, ct);

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
            .Select(p => new { p.Id, p.Name, p.UserId, p.IsGoalkeeper, JoinDate = p.JoinedAt ?? p.CreateDate })
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

        var recordMap     = records.ToLookup(r => r.Month);
        var monthlyFee    = settings?.MonthlyFee;
        var goalkeeperFee = settings?.GoalkeeperMonthlyFee;

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
            var effectiveFee = player.IsGoalkeeper
                ? (goalkeeperFee ?? monthlyFee ?? 0)
                : (monthlyFee ?? 0);
            return rec is null
                ? new MonthlyPaymentCellDto { Month = m, Status = PaymentStatus.Pending, Amount = effectiveFee }
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
            PlayerId     = player.Id,
            UserId       = player.UserId,
            PlayerName   = player.Name,
            IsGoalkeeper = player.IsGoalkeeper,
            JoinedYear   = joinYear,
            JoinedMonth  = joinMonth,
            Months       = months,
        });
    }

    public async Task<Result<PagedResultDto<ExtraChargeDto>>> GetMyExtraChargesAsync(
        Guid groupId, Guid userId, int? year = null, int? month = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        var empty = new PagedResultDto<ExtraChargeDto> { Page = page, PageSize = pageSize, Total = 0, Items = [] };

        var playerId = await _context.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId
                     && p.UserId  == userId
                     && !p.IsGuest)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        if (playerId is null) return Result<PagedResultDto<ExtraChargeDto>>.Ok(empty);

        var paymentsQuery = _context.ExtraChargePayments
            .AsNoTracking()
            .Where(ep => ep.GroupId  == groupId
                      && ep.PlayerId == playerId.Value);

        if (year.HasValue)
            paymentsQuery = paymentsQuery.Where(ep => ep.ExtraCharge!.CreateDate.Year == year.Value);
        if (month.HasValue)
            paymentsQuery = paymentsQuery.Where(ep => ep.ExtraCharge!.CreateDate.Month == month.Value);

        var total = await paymentsQuery.CountAsync(ct);

        var payments = await paymentsQuery
            .Include(ep => ep.ExtraCharge)
            .OrderByDescending(ep => ep.ExtraCharge!.CreateDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        if (payments.Count == 0) return Result<PagedResultDto<ExtraChargeDto>>.Ok(
            new PagedResultDto<ExtraChargeDto> { Page = page, PageSize = pageSize, Total = total, Items = [] });

        var playerName = await _context.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId.Value)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct) ?? "?";

        var names = new Dictionary<Guid, string> { [playerId.Value] = playerName };

        IReadOnlyList<ExtraChargeDto> items = payments
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

        return Result<PagedResultDto<ExtraChargeDto>>.Ok(new PagedResultDto<ExtraChargeDto>
        {
            Page = page, PageSize = pageSize, Total = total, Items = items,
        });
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

        // Pendências de mensalidade — mesma lógica do grid (getMyMonthlyRow):
        // • Usa JoinDate para não cobrar meses anteriores à entrada do jogador
        // • Conta meses sem registro Paid como Pending (independente do MonthlyFee)
        //   desde que a patota use cobrança mensal (MonthlyFee > 0) OU já existam
        //   registros mensais para esse jogador (fee pode ter sido zerado depois)
        int pendingMonths = 0;

        var playerInfo = await _context.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId && p.GroupId == groupId)
            .Select(p => new { JoinDate = p.JoinedAt ?? p.CreateDate })
            .FirstOrDefaultAsync(ct);

        if (playerInfo is not null)
        {
            var today      = DateTime.UtcNow;
            var maxMonth   = year == today.Year ? today.Month : 12;
            var joinYear   = playerInfo.JoinDate.Year;
            var joinMonth  = playerInfo.JoinDate.Month;
            var firstMonth = joinYear == year ? joinMonth
                           : joinYear >  year ? maxMonth + 1   // ainda não era membro
                           : 1;

            var hasRecordsOrFee = settings?.MonthlyFee > 0
                || await _context.MonthlyPayments
                    .AnyAsync(m => m.GroupId == groupId && m.PlayerId == playerId && m.Year == year, ct);

            if (firstMonth <= maxMonth && hasRecordsOrFee)
            {
                var paidMonths = await _context.MonthlyPayments
                    .AsNoTracking()
                    .Where(m => m.GroupId  == groupId
                             && m.PlayerId == playerId
                             && m.Year     == year
                             && m.Status   == PaymentStatus.Paid)
                    .Select(m => m.Month)
                    .ToListAsync(ct);

                pendingMonths = Enumerable.Range(firstMonth, maxMonth - firstMonth + 1)
                    .Count(m => !paidMonths.Contains(m));
            }
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

    // ── Notificações ──────────────────────────────────────────────────────────

    private async Task NotifyFinanceirosMonthlyPaidAsync(
        Guid groupId, Guid playerId, int month, int year, CancellationToken ct)
    {
        var playerName = await _context.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct);

        await _push.SendToGroupFinanceirosAsync(
            groupId,
            title: "Pagamento confirmado",
            body:  $"{playerName ?? "Jogador"} confirmou a mensalidade de {_monthNames[month - 1]}/{year}.",
            data:  new Dictionary<string, string> { ["type"] = "payment_confirmed", ["groupId"] = groupId.ToString() },
            ct);
    }

    private async Task NotifyPlayersExtraChargeCreatedAsync(
        IEnumerable<Guid?> playerUserIds, string chargeName, decimal amount, Guid groupId, CancellationToken ct)
    {
        var userIds = playerUserIds.Where(id => id.HasValue).Select(id => id!.Value).ToList();
        if (userIds.Count == 0) return;

        await _push.SendToUsersAsync(
            userIds,
            title: "Nova cobrança",
            body:  $"Você tem uma nova cobrança: \"{chargeName}\" — R$ {amount:N2}.",
            data:  new Dictionary<string, string> { ["type"] = "payment_pending", ["groupId"] = groupId.ToString() },
            ct,
            groupId: groupId);
    }

    private async Task NotifyFinanceirosExtraChargePaidAsync(
        Guid groupId, Guid playerId, Guid chargeId, CancellationToken ct)
    {
        var playerName = await _context.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct);

        var chargeName = await _context.ExtraCharges
            .AsNoTracking()
            .Where(c => c.Id == chargeId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);

        await _push.SendToGroupFinanceirosAsync(
            groupId,
            title: "Pagamento confirmado",
            body:  $"{playerName ?? "Jogador"} confirmou o pagamento de \"{chargeName ?? "cobrança extra"}\".",
            data:  new Dictionary<string, string> { ["type"] = "payment_confirmed", ["groupId"] = groupId.ToString() },
            ct);
    }

    /// <summary>
    /// Verifica se todos os pagamentos da cobrança extra estão quitados e, se sim, notifica os financeiros.
    /// Fire-and-forget — não bloqueia a resposta do endpoint.
    /// </summary>
    private async Task NotifyIfChargeFullyPaidAsync(Guid groupId, Guid chargeId, CancellationToken ct)
    {
        try
        {
            // Ignora cobranças canceladas
            var chargeExists = await _context.ExtraCharges
                .AsNoTracking()
                .AnyAsync(c => c.Id == chargeId && !c.IsCancelled, ct);

            if (!chargeExists) return;

            // Se ainda há algum pagamento pendente, não houve quitação total
            var anyPending = await _context.ExtraChargePayments
                .AsNoTracking()
                .AnyAsync(p => p.ExtraChargeId == chargeId
                            && p.GroupId       == groupId
                            && p.Status        == PaymentStatus.Pending, ct);

            if (anyPending) return;

            var chargeName = await _context.ExtraCharges
                .AsNoTracking()
                .Where(c => c.Id == chargeId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct);

            await _push.SendToGroupFinanceirosAsync(
                groupId,
                title: "Meta atingida!",
                body:  $"A cobrança \"{chargeName ?? "Cobrança"}\" foi totalmente paga pelo grupo.",
                data:  new Dictionary<string, string> { ["type"] = "charge_fully_paid", ["groupId"] = groupId.ToString() },
                ct);
        }
        catch { /* notificação não crítica */ }
    }

    private async Task NotifyPlayerMonthlyPendingAsync(
        Guid groupId, Guid playerId, int month, int year, CancellationToken ct)
    {
        var userId = await _context.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId)
            .Select(p => p.UserId)
            .FirstOrDefaultAsync(ct);

        if (userId is null) return;

        _logger.LogInformation(
            "[Payment] Transição Pago→Pendente: playerId={PlayerId} userId={UserId} month={Month}/{Year}",
            playerId, userId, month, year);

        await _push.SendToUserAsync(
            userId.Value,
            title: "Pendência financeira",
            body:  $"Sua mensalidade de {_monthNames[month - 1]}/{year} foi marcada como pendente.",
            data:  new Dictionary<string, string> { ["type"] = "payment_pending", ["groupId"] = groupId.ToString() },
            ct,
            groupId: groupId);
    }

    private async Task NotifyPlayerExtraChargePendingAsync(
        Guid groupId, Guid playerId, Guid chargeId, CancellationToken ct)
    {
        var userId = await _context.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId)
            .Select(p => p.UserId)
            .FirstOrDefaultAsync(ct);

        var chargeName = await _context.ExtraCharges
            .AsNoTracking()
            .Where(c => c.Id == chargeId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);

        if (userId is null) return;

        _logger.LogInformation(
            "[Payment] Transição Pago→Pendente (extra): playerId={PlayerId} userId={UserId} charge={ChargeName}",
            playerId, userId, chargeName);

        await _push.SendToUserAsync(
            userId.Value,
            title: "Pendência financeira",
            body:  $"Sua cobrança \"{chargeName ?? "extra"}\" foi marcada como pendente.",
            data:  new Dictionary<string, string> { ["type"] = "payment_pending", ["groupId"] = groupId.ToString() },
            ct,
            groupId: groupId);
    }

    // ── Pagar pendências em lote ──────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<PendingPaymentItemDto>>> GetMyPendingItemsAsync(
        Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var player = await _context.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId && p.UserId == userId && !p.IsGuest)
            .Select(p => new { p.Id, p.IsGoalkeeper, JoinDate = p.JoinedAt ?? p.CreateDate })
            .FirstOrDefaultAsync(ct);

        if (player is null)
            return Result<IReadOnlyList<PendingPaymentItemDto>>.Ok([]);

        var items = new List<PendingPaymentItemDto>();

        // ── Mensalidades ─────────────────────────────────────────────────────
        var today  = DateTime.UtcNow;
        var year   = today.Year;

        var settings = await _context.GroupSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.GroupId == groupId, ct);

        var maxMonth   = today.Month;
        var joinYear   = player.JoinDate.Year;
        var joinMonth  = player.JoinDate.Month;
        var firstMonth = joinYear == year ? joinMonth
                       : joinYear >  year ? maxMonth + 1
                       : 1;

        var hasRecordsOrFee = settings?.MonthlyFee > 0
            || await _context.MonthlyPayments
                .AnyAsync(m => m.GroupId == groupId && m.PlayerId == player.Id && m.Year == year, ct);

        if (firstMonth <= maxMonth && hasRecordsOrFee)
        {
            var records = await _context.MonthlyPayments
                .AsNoTracking()
                .Where(m => m.GroupId == groupId && m.PlayerId == player.Id && m.Year == year)
                .ToListAsync(ct);

            var recordMap = records.ToDictionary(r => r.Month);
            var fee = player.IsGoalkeeper
                ? (settings?.GoalkeeperMonthlyFee ?? settings?.MonthlyFee ?? 0m)
                : (settings?.MonthlyFee ?? 0m);

            for (var m = firstMonth; m <= maxMonth; m++)
            {
                recordMap.TryGetValue(m, out var rec);

                var amount   = rec?.Amount   ?? fee;
                var discount = rec?.Discount ?? 0m;
                var isPaid   = rec?.Status == PaymentStatus.Paid;

                items.Add(new PendingPaymentItemDto
                {
                    Id          = $"m-{year}-{m}",
                    Description = $"{_monthNames[m - 1]} {year}",
                    Amount      = amount,
                    Discount    = discount,
                    FinalAmount = Math.Max(0, amount - discount),
                    Type        = PendingPaymentType.Monthly,
                    Year        = year,
                    Month       = m,
                    IsPaid      = isPaid,
                });
            }
        }

        // ── Cobranças extras (pagas e pendentes) ─────────────────────────────
        var allExtras = await _context.ExtraChargePayments
            .AsNoTracking()
            .Include(ep => ep.ExtraCharge)
            .Where(ep => ep.GroupId  == groupId
                      && ep.PlayerId == player.Id
                      && !ep.ExtraCharge!.IsCancelled)
            .ToListAsync(ct);

        foreach (var ep in allExtras)
        {
            var effective = Math.Max(0, ep.Amount - ep.Discount);

            // Cobranças com desconto total (effective = 0) auto-pagas não têm
            // impacto no caixa e não fazem sentido aparecer no modal de pagamento.
            if (ep.Status == PaymentStatus.Paid && effective <= 0) continue;

            items.Add(new PendingPaymentItemDto
            {
                Id          = $"e-{ep.ExtraChargeId}",
                Description = ep.ExtraCharge!.Name,
                Amount      = ep.Amount,
                Discount    = ep.Discount,
                FinalAmount = effective,
                Type        = PendingPaymentType.Extra,
                ChargeId    = ep.ExtraChargeId,
                IsPaid      = ep.Status == PaymentStatus.Paid,
            });
        }

        return Result<IReadOnlyList<PendingPaymentItemDto>>.Ok(items);
    }

    public async Task<Result> PaySelectedAsync(
        Guid groupId, Guid userId, PaySelectedDto dto, CancellationToken ct = default)
    {
        if (dto.Items.Length == 0)
            return Result.Fail("Nenhum item selecionado.", ResultStatus.BadRequest);

        var player = await _context.Players
            .Where(p => p.GroupId == groupId && p.UserId == userId && !p.IsGuest)
            .Select(p => new { p.Id, p.IsGoalkeeper })
            .FirstOrDefaultAsync(ct);

        if (player is null)
            return Result.Fail("Jogador não encontrado nesta patota.", ResultStatus.NotFound);

        var settings = await _context.GroupSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.GroupId == groupId, ct);

        // Fetch player name and charge names needed for Caixa hooks
        var paySelectedPlayerName = await _context.Players
            .AsNoTracking()
            .Where(p => p.Id == player.Id)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct);

        var extraChargeIds = dto.Items
            .Where(i => i.Type == PendingPaymentType.Extra && i.ChargeId.HasValue)
            .Select(i => i.ChargeId!.Value)
            .Distinct()
            .ToList();

        var chargeNames = extraChargeIds.Count > 0
            ? await _context.ExtraCharges
                .AsNoTracking()
                .Where(c => extraChargeIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct)
            : new Dictionary<Guid, string>();

        var notifications    = new List<Func<Task>>();
        var transactionHooks = new List<Func<Task>>();

        foreach (var item in dto.Items)
        {
            if (item.Type == PendingPaymentType.Monthly
                && item.Year.HasValue && item.Month.HasValue)
            {
                var yr = item.Year.Value;
                var mo = item.Month.Value;

                var record = await _context.MonthlyPayments
                    .FirstOrDefaultAsync(m => m.GroupId  == groupId
                                           && m.PlayerId == player.Id
                                           && m.Year     == yr
                                           && m.Month    == mo, ct);

                if (record is null)
                {
                    // Só cria o registro se o objetivo é marcar como pago
                    if (!item.IsPaid) continue;
                    var newFee = player.IsGoalkeeper
                        ? (settings?.GoalkeeperMonthlyFee ?? settings?.MonthlyFee ?? 0)
                        : (settings?.MonthlyFee ?? 0);
                    record = new MonthlyPaymentEntity(groupId, player.Id, yr, mo, newFee);
                    await _context.MonthlyPayments.AddAsync(record, ct);
                }

                var wasPaid = record.Status == PaymentStatus.Paid;

                if (item.IsPaid)
                {
                    record.MarkAsPaid(null, null, null, null);
                    if (!wasPaid)
                        notifications.Add(() => NotifyFinanceirosMonthlyPaidAsync(groupId, player.Id, mo, yr, ct));
                }
                else
                {
                    record.MarkAsPending();
                    if (wasPaid)
                        notifications.Add(() => NotifyPlayerMonthlyPendingAsync(groupId, player.Id, mo, yr, ct));
                }

                // Sempre sincroniza o caixa — RecordOrRemove é idempotente
                var capturedRecord = record;
                transactionHooks.Add(() =>
                    _transactions.RecordOrRemovePaymentEntryAsync(
                        groupId,
                        TransactionSourceType.MonthlyPayment,
                        capturedRecord.Id,
                        capturedRecord.Amount - capturedRecord.Discount,
                        $"Mensalidade {_monthNames[mo - 1]}/{yr} – {paySelectedPlayerName}",
                        new DateOnly(yr, mo, 1),
                        item.IsPaid,
                        paySelectedPlayerName,
                        ct));
            }
            else if (item.Type == PendingPaymentType.Extra && item.ChargeId.HasValue)
            {
                var cid = item.ChargeId.Value;

                var payment = await _context.ExtraChargePayments
                    .FirstOrDefaultAsync(p => p.ExtraChargeId == cid
                                           && p.PlayerId     == player.Id
                                           && p.GroupId      == groupId, ct);

                if (payment is null) continue;

                var wasPaid = payment.Status == PaymentStatus.Paid;

                if (item.IsPaid)
                {
                    payment.MarkAsPaid(null, null, null, null);
                    if (!wasPaid)
                    {
                        var cidCapture = cid;
                        notifications.Add(() => NotifyFinanceirosExtraChargePaidAsync(groupId, player.Id, cidCapture, ct));
                        // Executado após SaveChangesAsync, de forma sequencial — sem concorrência no DbContext
                        notifications.Add(() => NotifyIfChargeFullyPaidAsync(groupId, cidCapture, ct));
                    }
                }
                else
                {
                    payment.MarkAsPending();
                    if (wasPaid)
                        notifications.Add(() => NotifyPlayerExtraChargePendingAsync(groupId, player.Id, cid, ct));
                }

                // Sempre sincroniza o caixa — RecordOrRemove é idempotente
                var capturedPayment = payment;
                var capturedName    = chargeNames.GetValueOrDefault(cid, "Cobrança");
                transactionHooks.Add(() =>
                    _transactions.RecordOrRemovePaymentEntryAsync(
                        groupId,
                        TransactionSourceType.ExtraCharge,
                        capturedPayment.Id,
                        capturedPayment.Amount - capturedPayment.Discount,
                        $"{capturedName} – {paySelectedPlayerName}",
                        capturedPayment.PaidAt.HasValue
                            ? DateOnly.FromDateTime(capturedPayment.PaidAt.Value)
                            : DateOnly.FromDateTime(DateTime.UtcNow),
                        item.IsPaid,
                        paySelectedPlayerName,
                        ct));
            }
        }

        await _context.SaveChangesAsync(ct);

        foreach (var hook in transactionHooks)
            await hook();

        foreach (var notify in notifications)
            await notify();

        return Result.Ok("Pagamentos confirmados com sucesso.");
    }

    // ── Limpeza (diagnóstico) ─────────────────────────────────────────────────

    public async Task<Result<(int MonthlyDeleted, int ExtraReset)>> ClearAllPaymentsAsync(
        Guid groupId, CancellationToken ct = default)
    {
        // 1) Remove todas as mensalidades do grupo
        var monthly = await _context.MonthlyPayments
            .Where(m => m.GroupId == groupId)
            .ToListAsync(ct);

        _context.MonthlyPayments.RemoveRange(monthly);

        // 2) Reseta cobranças extras para Pendente via SQL direto (private setters)
        var extraReset = await _context.Database.ExecuteSqlRawAsync(
            @"UPDATE ""ExtraChargePayments""
              SET ""Status""         = 0,
                  ""PaidAt""         = NULL,
                  ""Discount""       = 0,
                  ""DiscountReason"" = NULL,
                  ""ProofBase64""    = NULL,
                  ""ProofFileName""  = NULL,
                  ""ProofMimeType""  = NULL,
                  ""MarkedByAdminId"" = NULL
              WHERE ""GroupId"" = {0}",
            groupId);

        // 3) Remove lançamentos do caixa
        await _transactions.ClearAllTransactionsAsync(groupId, ct);

        await _context.SaveChangesAsync(ct);

        return Result<(int MonthlyDeleted, int ExtraReset)>.Ok(
            (monthly.Count, extraReset),
            $"{monthly.Count} mensalidade(s) removida(s), {extraReset} cobrança(s) extra resetada(s).");
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
