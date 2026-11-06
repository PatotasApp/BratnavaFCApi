namespace BratnavaFC.Domain.Enums;

/// <summary>Loja onde a assinatura da patota foi comprada.</summary>
public enum BillingStore
{
    Apple = 1,
    Google = 2,

    /// <summary>
    /// Compra simulada, só para teste local. O validador falso só existe em Development
    /// com <c>Billing:Simulated:Enabled</c> ligado; fora disso a API recusa a loja.
    /// </summary>
    Simulated = 3,
}

/// <summary>Situação da patota em relação ao pagamento.</summary>
public enum GroupBillingStatus
{
    /// <summary>Isenção permanente (ex.: Bratnava FC).</summary>
    Lifetime = 1,
    /// <summary>Dentro do teste grátis (ou de uma cortesia dada pelo GodMode).</summary>
    Trial = 2,
    /// <summary>Período pago em vigor.</summary>
    Active = 3,
    /// <summary>Venceu, mas ainda dentro da tolerância: continua liberada.</summary>
    Grace = 4,
    /// <summary>Venceu e passou da tolerância: somente leitura (se o interruptor estiver ligado).</summary>
    Expired = 5,
}

/// <summary>Estado de uma transação (compra ou renovação) vinda da loja.</summary>
public enum BillingPurchaseState
{
    /// <summary>Confirmada na loja; conta para o período pago.</summary>
    Confirmed = 1,
    /// <summary>Pagamento ainda pendente na loja (ex.: boleto no Google); não libera nada.</summary>
    Pending = 2,
    /// <summary>Reembolsada ou revogada; o período sai da conta.</summary>
    Refunded = 3,
}

/// <summary>Estado da assinatura (o "contrato" com renovação automática) na loja.</summary>
public enum StoreSubscriptionState
{
    Active = 1,
    /// <summary>A loja não conseguiu cobrar e está tentando de novo; a loja ainda dá acesso.</summary>
    GracePeriod = 2,
    /// <summary>A loja não conseguiu cobrar e não dá mais acesso enquanto tenta.</summary>
    BillingRetry = 3,
    Expired = 4,
    Revoked = 5,
    Paused = 6,
    Pending = 7,
}

public enum StoreEventStatus
{
    Received = 1,
    Processed = 2,
    Ignored = 3,
    Failed = 4,
}

/// <summary>Tipo de ação registrada na auditoria de cobrança.</summary>
public enum BillingAdjustmentType
{
    ExtendDays = 1,
    SetLifetime = 2,
    RemoveLifetime = 3,
    Reprocess = 4,
    SettingsChanged = 5,
    ProductChanged = 6,
    SimulatedEvent = 7,
}
