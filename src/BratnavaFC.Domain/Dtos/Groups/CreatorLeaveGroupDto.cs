namespace BratnavaFC.Domain.Dtos.Groups;

/// <summary>
/// Transfere a liderança e converte o criador em convidado,
/// ou deleta o grupo.
/// Exatamente uma das opções deve ser preenchida.
/// </summary>
public record CreatorLeaveGroupDto(
    Guid? TransferToUserId = null,
    Guid? PromoteAndTransferUserId = null,
    bool DeleteGroup = false
);
