namespace BratnavaFC.Domain.Entities;

public class PushTokenEntity : BaseEntity
{
    public Guid UserId { get; private set; }
    public UserEntity? User { get; private set; }
    public string Token { get; private set; } = null!;
    public string Platform { get; private set; } = null!; // "android" | "ios"
    public bool IsActive { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private PushTokenEntity() { }

    public PushTokenEntity(Guid userId, string token, string platform)
    {
        if (userId == Guid.Empty)
            throw new InvalidOperationException("UserId é obrigatório.");
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Token é obrigatório.");
        if (string.IsNullOrWhiteSpace(platform))
            throw new InvalidOperationException("Platform é obrigatória.");

        UserId = userId;
        Token = token.Trim();
        Platform = platform.ToLowerInvariant().Trim();
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Reativa o token (ex.: usuário fez login novamente no mesmo dispositivo).</summary>
    public void Reactivate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Marca o token como inativo (ex.: token expirado/inválido no FCM).</summary>
    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Atualiza o timestamp sem alterar outros campos (heartbeat).</summary>
    public void Touch()
    {
        UpdatedAt = DateTime.UtcNow;
    }
}
