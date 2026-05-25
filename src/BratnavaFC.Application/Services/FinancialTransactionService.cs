using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Transactions;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class FinancialTransactionService : IFinancialTransactionService
{
    private readonly AppDbContext _db;

    private static readonly string[] _monthNames =
    [
        "Janeiro", "Fevereiro", "Março",    "Abril",
        "Maio",    "Junho",     "Julho",    "Agosto",
        "Setembro","Outubro",   "Novembro", "Dezembro"
    ];

    public FinancialTransactionService(AppDbContext db)
    {
        _db = db;
    }

    // ── Consultas ────────────────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<TransactionDto>>> GetByMonthAsync(
        Guid groupId, int year, int month, CancellationToken ct = default)
    {
        // Use date range instead of .Year/.Month to guarantee PostgreSQL translation
        var from = new DateOnly(year, month, 1);
        var to   = from.AddMonths(1);

        var transactions = await _db.GroupTransactions
            .AsNoTracking()
            .Where(t => t.GroupId == groupId
                     && t.Date >= from
                     && t.Date <  to)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.CreateDate)
            .ToListAsync(ct);

        IReadOnlyList<TransactionDto> result = transactions.Select(ToDto).ToList();
        return Result<IReadOnlyList<TransactionDto>>.Ok(result);
    }

    public async Task<Result<IReadOnlyList<TransactionMonthSummaryDto>>> GetMonthlySummariesAsync(
        Guid groupId, CancellationToken ct = default)
    {
        // Fetch all rows ordered by date — group in-memory to avoid DateOnly
        // property translation issues and to compute accumulated balance cleanly.
        var all = await _db.GroupTransactions
            .AsNoTracking()
            .Where(t => t.GroupId == groupId)
            .OrderBy(t => t.Date)
            .Select(t => new { t.Date.Year, t.Date.Month, t.Type, t.Amount })
            .ToListAsync(ct);

        var rows = all
            .GroupBy(t => new { t.Year, t.Month })
            .OrderBy(g => g.Key.Year)
            .ThenBy(g => g.Key.Month)
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                TotalIncome  = g.Where(t => t.Type == TransactionType.Income) .Sum(t => t.Amount),
                TotalExpense = g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount),
            })
            .ToList();

        var summaries  = new List<TransactionMonthSummaryDto>(rows.Count);
        var accumulated = 0m;

        foreach (var r in rows)
        {
            var net = r.TotalIncome - r.TotalExpense;
            accumulated += net;
            summaries.Add(new TransactionMonthSummaryDto(
                r.Year, r.Month,
                r.TotalIncome, r.TotalExpense,
                net, accumulated));
        }

        return Result<IReadOnlyList<TransactionMonthSummaryDto>>.Ok(summaries);
    }

    public async Task<Result<PendingTotalsDto>> GetPendingTotalsAsync(
        Guid groupId, CancellationToken ct = default)
    {
        var today    = DateTime.UtcNow;
        var year     = today.Year;
        var maxMonth = today.Month;

        var settings = await _db.GroupSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.GroupId == groupId, ct);

        var monthlyFee    = settings?.MonthlyFee       ?? 0m;
        var goalkeeperFee = settings?.GoalkeeperMonthlyFee;

        // Mensalistas ativos (mesma definição usada no grid e em GetMyPendingItems)
        var players = await _db.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId
                     && !p.IsGuest
                     && p.UserId  != null
                     && p.Status  == Status.Active)
            .Select(p => new { p.Id, p.IsGoalkeeper, JoinDate = p.JoinedAt ?? p.CreateDate })
            .ToListAsync(ct);

        decimal monthlyPending = 0m;

        if (players.Count > 0)
        {
            var playerIds = players.Select(p => p.Id).ToList();

            // Todos os registros do ano corrente (Pending e Paid)
            var records = await _db.MonthlyPayments
                .AsNoTracking()
                .Where(m => m.GroupId == groupId
                         && playerIds.Contains(m.PlayerId)
                         && m.Year    == year)
                .Select(m => new { m.PlayerId, m.Month, m.Status, m.Amount, m.Discount })
                .ToListAsync(ct);

            var recordsByPlayer = records.ToLookup(r => r.PlayerId);

            foreach (var player in players)
            {
                var joinYear   = player.JoinDate.Year;
                var joinMonth  = player.JoinDate.Month;
                var firstMonth = joinYear == year ? joinMonth
                               : joinYear >  year ? maxMonth + 1
                               : 1;

                if (firstMonth > maxMonth) continue;

                var standardFee = player.IsGoalkeeper
                    ? (goalkeeperFee ?? monthlyFee)
                    : monthlyFee;

                var playerRecords = recordsByPlayer[player.Id]
                    .ToDictionary(r => r.Month);

                // Só processa se há mensalidade configurada OU se já existem registros
                var hasRecordsOrFee = standardFee > 0 || playerRecords.Count > 0;
                if (!hasRecordsOrFee) continue;

                for (var m = firstMonth; m <= maxMonth; m++)
                {
                    if (playerRecords.TryGetValue(m, out var rec))
                    {
                        if (rec.Status == PaymentStatus.Paid) continue;
                        // Pending explícito (pode ter desconto parcial)
                        monthlyPending += Math.Max(0, rec.Amount - rec.Discount);
                    }
                    else
                    {
                        // Mês sem registro → pendência virtual com a mensalidade padrão
                        monthlyPending += standardFee;
                    }
                }
            }
        }

        var extraPending = await _db.ExtraChargePayments
            .AsNoTracking()
            .Where(p => p.GroupId == groupId
                     && p.Status  == PaymentStatus.Pending
                     && !p.ExtraCharge!.IsCancelled)
            .SumAsync(p => p.Amount - p.Discount, ct);

        return Result<PendingTotalsDto>.Ok(new PendingTotalsDto(
            monthlyPending,
            extraPending,
            monthlyPending + extraPending));
    }

    // ── Escrita ──────────────────────────────────────────────────────────────

    public async Task<Result<TransactionDto>> CreateManualAsync(
        Guid groupId,
        CreateTransactionDto dto,
        Guid createdByUserId,
        CancellationToken ct = default)
    {
        if (dto.Amount <= 0)
            return Result<TransactionDto>.Fail("Valor deve ser positivo.", ResultStatus.BadRequest);

        if (string.IsNullOrWhiteSpace(dto.Description))
            return Result<TransactionDto>.Fail("Descrição é obrigatória.", ResultStatus.BadRequest);

        if (dto.Type == TransactionType.Expense && dto.Category is null)
            return Result<TransactionDto>.Fail("Categoria é obrigatória para saídas.", ResultStatus.BadRequest);

        var entity = new GroupTransactionEntity(
            groupId,
            dto.Type,
            dto.Amount,
            dto.Description.Trim(),
            dto.Date,
            createdByUserId,
            dto.Category);

        await _db.GroupTransactions.AddAsync(entity, ct);
        await _db.SaveChangesAsync(ct);

        return Result<TransactionDto>.Ok(ToDto(entity), "Lançamento criado com sucesso.", ResultStatus.Created);
    }

    public async Task<Result> DeleteManualAsync(
        Guid groupId, Guid transactionId, CancellationToken ct = default)
    {
        var entity = await _db.GroupTransactions
            .FirstOrDefaultAsync(t => t.Id == transactionId && t.GroupId == groupId, ct);

        if (entity is null)
            return Result.Fail("Lançamento não encontrado.", ResultStatus.NotFound);

        if (entity.IsAutomatic)
            return Result.Fail("Lançamentos automáticos não podem ser excluídos diretamente.", ResultStatus.BadRequest);

        _db.GroupTransactions.Remove(entity);
        await _db.SaveChangesAsync(ct);

        return Result.Ok("Lançamento excluído com sucesso.");
    }

    // ── Integração com pagamentos ─────────────────────────────────────────────

    public async Task RecordOrRemovePaymentEntryAsync(
        Guid                  groupId,
        TransactionSourceType sourceType,
        Guid                  sourceId,
        decimal               amount,
        string                description,
        DateOnly              date,
        bool                  isPaid,
        string?               playerName,
        CancellationToken     ct = default)
    {
        var existing = await _db.GroupTransactions
            .FirstOrDefaultAsync(t => t.GroupId    == groupId
                                   && t.SourceType == sourceType
                                   && t.SourceId   == sourceId, ct);

        if (isPaid)
        {
            if (existing is null)
            {
                // Pagamento com desconto total (amount = 0) não gera entrada no caixa
                if (amount <= 0) return;

                var entry = new GroupTransactionEntity(
                    groupId, amount, description, date, sourceType, sourceId, playerName);
                await _db.GroupTransactions.AddAsync(entry, ct);
                await _db.SaveChangesAsync(ct);
            }
            else if (amount <= 0)
            {
                // Desconto total aplicado após o pagamento — remove a entrada existente
                _db.GroupTransactions.Remove(existing);
                await _db.SaveChangesAsync(ct);
            }
            else if (existing.Amount != amount)
            {
                // Valor mudou (ex: desconto parcial aplicado) — substitui pelo valor correto
                _db.GroupTransactions.Remove(existing);
                var updated = new GroupTransactionEntity(
                    groupId, amount, description, date, sourceType, sourceId, playerName);
                _db.GroupTransactions.Add(updated);
                await _db.SaveChangesAsync(ct);
            }
            // else: mesmo valor e já pago — no-op
        }
        else if (existing is not null)
        {
            _db.GroupTransactions.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
        // else: not paid and no existing entry — no-op
    }

    // ── Sincronização retroativa ──────────────────────────────────────────────

    public async Task<Result<SyncResultDto>> SyncPaidPaymentsAsync(
        Guid groupId, CancellationToken ct = default)
    {
        // Snapshot de todos os lançamentos automáticos existentes para o grupo
        var existingTx = await _db.GroupTransactions
            .Where(t => t.GroupId == groupId && t.IsAutomatic && t.SourceId != null)
            .Select(t => new { t.Id, SourceId = t.SourceId!.Value, t.Amount, t.SourceType })
            .ToListAsync(ct);

        var txByMonthly = existingTx
            .Where(t => t.SourceType == TransactionSourceType.MonthlyPayment)
            .ToDictionary(t => t.SourceId);
        var txByExtra = existingTx
            .Where(t => t.SourceType == TransactionSourceType.ExtraCharge)
            .ToDictionary(t => t.SourceId);

        var idsToDelete = new List<Guid>();
        var created = 0;
        var updated = 0;

        // ── Mensalidades pagas ────────────────────────────────────────────────
        var paidMonthly = await _db.MonthlyPayments
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == PaymentStatus.Paid)
            .Select(m => new { m.Id, m.PlayerId, m.Amount, m.Discount, m.Year, m.Month })
            .ToListAsync(ct);

        if (paidMonthly.Count > 0)
        {
            var playerIds = paidMonthly.Select(m => m.PlayerId).Distinct().ToList();
            var playerMap = await _db.Players.AsNoTracking()
                .Where(p => playerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

            foreach (var m in paidMonthly)
            {
                var effective = m.Amount - m.Discount;
                txByMonthly.TryGetValue(m.Id, out var existing);

                if (existing is null)
                {
                    if (effective <= 0) continue;   // desconto total — sem fluxo de caixa
                    var name = playerMap.GetValueOrDefault(m.PlayerId, "Jogador");
                    _db.GroupTransactions.Add(new GroupTransactionEntity(
                        groupId, effective,
                        $"Mensalidade {_monthNames[m.Month - 1]}/{m.Year} – {name}",
                        new DateOnly(m.Year, m.Month, 1),
                        TransactionSourceType.MonthlyPayment, m.Id, name));
                    created++;
                }
                else if (effective <= 0)
                {
                    // Desconto total aplicado após o pagamento — remove entrada existente
                    idsToDelete.Add(existing.Id);
                    updated++;
                }
                else if (existing.Amount != effective)
                {
                    // Valor diverge (desconto parcial) — remove e recria com valor correto
                    idsToDelete.Add(existing.Id);
                    var name = playerMap.GetValueOrDefault(m.PlayerId, "Jogador");
                    _db.GroupTransactions.Add(new GroupTransactionEntity(
                        groupId, effective,
                        $"Mensalidade {_monthNames[m.Month - 1]}/{m.Year} – {name}",
                        new DateOnly(m.Year, m.Month, 1),
                        TransactionSourceType.MonthlyPayment, m.Id, name));
                    updated++;
                }
                // else: valor idêntico — no-op
            }
        }

        // ── Cobranças extras pagas ────────────────────────────────────────────
        var paidExtra = await (
            from p in _db.ExtraChargePayments
            join c in _db.ExtraCharges on p.ExtraChargeId equals c.Id
            where p.GroupId == groupId
               && p.Status  == PaymentStatus.Paid
               && !c.IsCancelled
            select new { p.Id, p.PlayerId, p.ExtraChargeId, p.Amount, p.Discount, p.PaidAt, ChargeName = c.Name }
        ).AsNoTracking().ToListAsync(ct);

        if (paidExtra.Count > 0)
        {
            var playerIds = paidExtra.Select(e => e.PlayerId).Distinct().ToList();
            var playerMap = await _db.Players.AsNoTracking()
                .Where(p => playerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

            foreach (var e in paidExtra)
            {
                var effective = e.Amount - e.Discount;
                txByExtra.TryGetValue(e.Id, out var existing);

                if (existing is null)
                {
                    if (effective <= 0) continue;
                    var playerName = playerMap.GetValueOrDefault(e.PlayerId, "Jogador");
                    _db.GroupTransactions.Add(new GroupTransactionEntity(
                        groupId, effective,
                        $"{e.ChargeName} – {playerName}",
                        e.PaidAt.HasValue
                            ? DateOnly.FromDateTime(e.PaidAt.Value)
                            : DateOnly.FromDateTime(DateTime.UtcNow),
                        TransactionSourceType.ExtraCharge, e.Id, playerName));
                    created++;
                }
                else if (effective <= 0)
                {
                    idsToDelete.Add(existing.Id);
                    updated++;
                }
                else if (existing.Amount != effective)
                {
                    idsToDelete.Add(existing.Id);
                    var playerName = playerMap.GetValueOrDefault(e.PlayerId, "Jogador");
                    _db.GroupTransactions.Add(new GroupTransactionEntity(
                        groupId, effective,
                        $"{e.ChargeName} – {playerName}",
                        e.PaidAt.HasValue
                            ? DateOnly.FromDateTime(e.PaidAt.Value)
                            : DateOnly.FromDateTime(DateTime.UtcNow),
                        TransactionSourceType.ExtraCharge, e.Id, playerName));
                    updated++;
                }
            }
        }

        // Aplica deleções + criações em uma única transação
        if (idsToDelete.Count > 0)
        {
            var toDelete = await _db.GroupTransactions
                .Where(t => idsToDelete.Contains(t.Id))
                .ToListAsync(ct);
            _db.GroupTransactions.RemoveRange(toDelete);
        }

        if (created > 0 || updated > 0)
            await _db.SaveChangesAsync(ct);

        var msg = (created, updated) switch
        {
            (0, 0) => "Caixa já está sincronizado.",
            (_, 0) => $"{created} lançamento(s) criado(s).",
            (0, _) => $"{updated} lançamento(s) atualizado(s).",
            _      => $"{created} criado(s), {updated} atualizado(s).",
        };

        return Result<SyncResultDto>.Ok(new SyncResultDto(created, updated), msg);
    }

    // ── Limpeza (diagnóstico) ─────────────────────────────────────────────────

    public async Task<Result<int>> ClearAllTransactionsAsync(
        Guid groupId, CancellationToken ct = default)
    {
        var all = await _db.GroupTransactions
            .Where(t => t.GroupId == groupId)
            .ToListAsync(ct);

        if (all.Count == 0)
            return Result<int>.Ok(0, "Caixa já estava vazio.");

        _db.GroupTransactions.RemoveRange(all);
        await _db.SaveChangesAsync(ct);

        return Result<int>.Ok(all.Count, $"{all.Count} lançamento(s) removido(s).");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TransactionDto ToDto(GroupTransactionEntity t) => new(
        t.Id,
        t.Type,
        t.Amount,
        t.Description,
        t.Date,
        t.Category,
        t.IsAutomatic,
        t.SourceType,
        t.PlayerName,
        t.CreateDate);
}
