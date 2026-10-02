namespace BratnavaFC.Domain.Entities;

public sealed class ExtraChargeEntity : BaseEntity
{
    private ExtraChargeEntity() { } // EF

    public ExtraChargeEntity(
        Guid groupId,
        string name,
        string? description,
        decimal amount,
        DateOnly? dueDate,
        Guid createdByAdminId)
    {
        if (groupId == Guid.Empty)      throw new InvalidOperationException("GroupId é obrigatório.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Nome é obrigatório.");
        if (amount <= 0)                throw new InvalidOperationException("Valor deve ser maior que zero.");

        GroupId          = groupId;
        Name             = name.Trim();
        Description      = description?.Trim();
        Amount           = amount;
        DueDate          = dueDate;
        CreatedByAdminId = createdByAdminId;
        IsCancelled      = false;
    }

    public Guid    GroupId          { get; private set; }
    public string  Name             { get; private set; } = string.Empty;
    public string? Description      { get; private set; }
    public decimal Amount           { get; private set; }
    public DateOnly? DueDate        { get; private set; }
    /// <summary>Admin que criou a cobrança. Null quando ele excluiu a conta.</summary>
    public Guid?   CreatedByAdminId { get; private set; }
    public bool    IsCancelled      { get; private set; }

    // Navegação
    public GroupEntity?                   Group    { get; private set; }
    public List<ExtraChargePaymentEntity> Payments { get; private set; } = [];

    public void Cancel()     => IsCancelled = true;
    public void Reactivate() => IsCancelled = false;

    public void UpdateDetails(string? name, string? description, decimal? amount)
    {
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Nome é obrigatório.");
            Name = name.Trim();
        }
        if (description is not null)
            Description = description.Trim() == string.Empty ? null : description.Trim();
        if (amount.HasValue)
        {
            if (amount.Value <= 0) throw new InvalidOperationException("Valor deve ser maior que zero.");
            Amount = amount.Value;
        }
    }
}
