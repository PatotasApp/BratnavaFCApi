namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Registro append-only de uma decisão de participação. O estado atual continua nas tabelas
/// de partida/enquete, enquanto esta tabela preserva todas as mudanças para auditoria.
/// </summary>
public sealed class ParticipationActionHistoryEntity : BaseEntity
{
    private ParticipationActionHistoryEntity() { }

    public ParticipationActionHistoryEntity(
        Guid groupId,
        string subjectType,
        Guid subjectId,
        Guid playerId,
        string action,
        string? details = null,
        Guid? performedByUserId = null,
        bool performedByAdmin = false)
    {
        if (groupId == Guid.Empty || subjectId == Guid.Empty || playerId == Guid.Empty)
            throw new ArgumentException("Grupo, item e jogador são obrigatórios.");

        GroupId = groupId;
        SubjectType = Required(subjectType, 20);
        SubjectId = subjectId;
        PlayerId = playerId;
        Action = Required(action, 40);
        Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        PerformedByUserId = performedByUserId;
        PerformedByAdmin = performedByAdmin;
    }

    public Guid GroupId { get; private set; }
    public string SubjectType { get; private set; } = string.Empty;
    public Guid SubjectId { get; private set; }
    public Guid PlayerId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? Details { get; private set; }
    public Guid? PerformedByUserId { get; private set; }
    public bool PerformedByAdmin { get; private set; }

    private static string Required(string value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) throw new ArgumentException("Valor obrigatório.");
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
