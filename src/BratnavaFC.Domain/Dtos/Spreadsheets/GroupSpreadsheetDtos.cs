using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Spreadsheets;

/// <summary>
/// Uma planilha enviada. O mesmo formato serve para a patota e para o painel dos
/// administradores da plataforma; o e-mail de quem enviou só vem preenchido no painel.
/// </summary>
public sealed record GroupSpreadsheetDto(
    Guid Id,
    Guid GroupId,
    string GroupName,
    GroupSpreadsheetKind Kind,
    string FileName,
    long SizeBytes,
    string? Note,
    GroupSpreadsheetStatus Status,
    string? AdminNote,
    Guid UploadedByUserId,
    string UploadedByName,
    string? UploadedByEmail,
    DateTime CreatedAt,
    DateTime? StatusChangedAt);

/// <summary>Troca de situação feita no painel. A observação é opcional e substitui a anterior.</summary>
public sealed record ChangeGroupSpreadsheetStatusDto(GroupSpreadsheetStatus Status, string? AdminNote);

/// <summary>Arquivo pronto para download (quem recebe fecha o stream).</summary>
public sealed record GroupSpreadsheetDownload(Stream Content, string ContentType, string FileName);
