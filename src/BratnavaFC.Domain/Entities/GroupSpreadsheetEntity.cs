using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Planilha que a patota mandou (histórico de jogos ou pagamentos) para um administrador
/// da plataforma importar por fora. Não há importação automática: a linha guarda onde o
/// arquivo está no storage privado e em que pé está a importação.
/// </summary>
public sealed class GroupSpreadsheetEntity : BaseEntity
{
    public const int FileNameMaxLength = 200;
    public const int StorageKeyMaxLength = 300;
    public const int ContentTypeMaxLength = 120;
    public const int NoteMaxLength = 1000;

    private GroupSpreadsheetEntity() { }

    public GroupSpreadsheetEntity(
        Guid groupId, Guid uploadedByUserId, GroupSpreadsheetKind kind,
        string originalFileName, string storageKey, string contentType, long sizeBytes, string? note)
    {
        if (groupId == Guid.Empty) throw new ArgumentException("A patota é obrigatória.");
        if (uploadedByUserId == Guid.Empty) throw new ArgumentException("Quem enviou é obrigatório.");
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Tipo de planilha inválido.");
        if (string.IsNullOrWhiteSpace(originalFileName)) throw new ArgumentException("O nome do arquivo é obrigatório.");
        if (string.IsNullOrWhiteSpace(storageKey)) throw new ArgumentException("A chave do arquivo é obrigatória.");
        if (sizeBytes <= 0) throw new ArgumentException("O arquivo está vazio.");

        GroupId = groupId;
        UploadedByUserId = uploadedByUserId;
        Kind = kind;
        OriginalFileName = Truncate(originalFileName.Trim(), FileNameMaxLength)!;
        StorageKey = storageKey;
        ContentType = Truncate(contentType, ContentTypeMaxLength) ?? "application/octet-stream";
        SizeBytes = sizeBytes;
        Note = Truncate(note, NoteMaxLength);
        Status = GroupSpreadsheetStatus.Received;
    }

    public Guid GroupId { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public GroupSpreadsheetKind Kind { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }

    /// <summary>Observação de quem enviou ("só 2024", "a aba certa é a segunda"...).</summary>
    public string? Note { get; private set; }

    public GroupSpreadsheetStatus Status { get; private set; }

    /// <summary>Observação do administrador da plataforma, visível para a patota.</summary>
    public string? AdminNote { get; private set; }

    public DateTime? StatusChangedAt { get; private set; }

    /// <summary>Sem chave estrangeira, como os outros "quem fez" de auditoria.</summary>
    public Guid? StatusChangedByUserId { get; private set; }

    /// <summary>
    /// Muda a situação. Devolve true quando ela de fato mudou — é o que decide se quem
    /// enviou recebe aviso. Voltar para trás é permitido (correção de clique errado).
    /// </summary>
    public bool ChangeStatus(GroupSpreadsheetStatus status, Guid changedByUserId, string? adminNote)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentException("Situação inválida.");

        var changed = status != Status;
        Status = status;
        if (adminNote is not null)
            AdminNote = Truncate(adminNote, NoteMaxLength);
        StatusChangedAt = DateTime.UtcNow;
        StatusChangedByUserId = changedByUserId;
        UpdateDate = DateTime.UtcNow;
        return changed;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
