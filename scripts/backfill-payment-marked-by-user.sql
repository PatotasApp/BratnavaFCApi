-- Backfill de auditoria de pagamentos antigos.
--
-- Objetivo:
--   Para pagamentos ja marcados como pagos antes da coluna MarkedByUserId existir,
--   preencher MarkedByUserId com o UserId do proprio jogador devedor.
--
-- Observacoes:
--   - Status = 1 representa pagamento pago.
--   - Jogadores convidados sem UserId sao ignorados.
--   - O script nao altera MarkedByAdminId, pois a intencao aqui e apenas
--     identificar a pessoa dona da pendencia quando nao ha auditoria antiga.

BEGIN;

WITH updated_monthly AS (
    UPDATE "MonthlyPayments" mp
       SET "MarkedByUserId" = p."UserId"
      FROM "Players" p
     WHERE mp."PlayerId" = p."Id"
       AND mp."GroupId" = p."GroupId"
       AND mp."Status" = 1
       AND mp."MarkedByUserId" IS NULL
       AND p."UserId" IS NOT NULL
     RETURNING mp."Id"
),
updated_extra AS (
    UPDATE "ExtraChargePayments" ecp
       SET "MarkedByUserId" = p."UserId"
      FROM "Players" p
     WHERE ecp."PlayerId" = p."Id"
       AND ecp."GroupId" = p."GroupId"
       AND ecp."Status" = 1
       AND ecp."MarkedByUserId" IS NULL
       AND p."UserId" IS NOT NULL
     RETURNING ecp."Id"
)
SELECT
    (SELECT COUNT(*) FROM updated_monthly) AS monthly_payments_updated,
    (SELECT COUNT(*) FROM updated_extra) AS extra_charge_payments_updated;

COMMIT;
