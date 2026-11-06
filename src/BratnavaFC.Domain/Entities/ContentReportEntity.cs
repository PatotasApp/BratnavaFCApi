using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Denúncia de conteúdo feita por um usuário (exigência da App Store, regra 1.2,
/// para apps com conteúdo criado por usuários). Fica pendente até um admin da
/// patota ou da plataforma resolver.
/// </summary>
public sealed class ContentReportEntity : BaseEntity
{
    public const int ReasonMaxLength = 60;
    public const int DetailsMaxLength = 1000;

    private ContentReportEntity() { }

    public ContentReportEntity(
        Guid reporterUserId, Guid? groupId, ContentReportTargetType targetType, Guid targetId,
        string reason, string? details)
    {
        if (reporterUserId == Guid.Empty) throw new ArgumentException("Quem denuncia é obrigatório.");
        if (targetId == Guid.Empty) throw new ArgumentException("O conteúdo denunciado é obrigatório.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Informe o motivo da denúncia.");

        ReporterUserId = reporterUserId;
        GroupId = groupId;
        TargetType = targetType;
        TargetId = targetId;
        Reason = reason.Trim()[..Math.Min(reason.Trim().Length, ReasonMaxLength)];
        Details = string.IsNullOrWhiteSpace(details)
            ? null
            : details.Trim()[..Math.Min(details.Trim().Length, DetailsMaxLength)];
    }

    public Guid ReporterUserId { get; private set; }
    public Guid? GroupId { get; private set; }
    public ContentReportTargetType TargetType { get; private set; }
    public Guid TargetId { get; private set; }
    public string Reason { get; private set; } = null!;
    public string? Details { get; private set; }
    public bool IsResolved { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public string? ResolutionNote { get; private set; }

    public void Resolve(Guid resolvedByUserId, string? note)
    {
        IsResolved = true;
        ResolvedAt = DateTime.UtcNow;
        ResolvedByUserId = resolvedByUserId;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, DetailsMaxLength)];
        UpdateDate = DateTime.UtcNow;
    }
}
