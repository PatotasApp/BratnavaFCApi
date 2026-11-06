namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Um usuário bloqueou outro (exigência da App Store, regra 1.2). O bloqueio vale
/// nos dois sentidos para o perfil público: nenhum dos dois vê o do outro.
/// </summary>
public sealed class UserBlockEntity : BaseEntity
{
    private UserBlockEntity() { }

    public UserBlockEntity(Guid blockerUserId, Guid blockedUserId)
    {
        if (blockerUserId == Guid.Empty || blockedUserId == Guid.Empty)
            throw new ArgumentException("Usuário inválido.");
        if (blockerUserId == blockedUserId)
            throw new ArgumentException("Você não pode bloquear a si mesmo.");

        BlockerUserId = blockerUserId;
        BlockedUserId = blockedUserId;
    }

    public Guid BlockerUserId { get; private set; }
    public Guid BlockedUserId { get; private set; }
}
