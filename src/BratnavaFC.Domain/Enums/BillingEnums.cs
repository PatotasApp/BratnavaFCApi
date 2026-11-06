namespace BratnavaFC.Domain.Enums;

/// <summary>Loja onde o pacote da patota foi comprado.</summary>
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
    /// <summary>Dentro do teste grátis (ou de dias dados pelo GodMode, sem pacote pago).</summary>
    Trial = 2,
    /// <summary>Período pago em vigor.</summary>
    Active = 3,
    /// <summary>Venceu, mas ainda dentro da tolerância: continua liberada.</summary>
    Grace = 4,
    /// <summary>Venceu e passou da tolerância: somente leitura (se o interruptor estiver ligado).</summary>
    Expired = 5,
}

/// <summary>Estado de uma compra de pacote vinda da loja.</summary>
public enum BillingPurchaseState
{
    /// <summary>Confirmada na loja; soma os meses ao vencimento.</summary>
    Confirmed = 1,
    /// <summary>Pagamento ainda pendente na loja (ex.: boleto no Google); não soma nada.</summary>
    Pending = 2,
    /// <summary>Reembolsada ou cancelada; os meses saem da conta.</summary>
    Refunded = 3,
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
