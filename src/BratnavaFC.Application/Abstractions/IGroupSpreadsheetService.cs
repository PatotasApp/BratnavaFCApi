using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Spreadsheets;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Application.Abstractions;

/// <summary>
/// Planilhas que a patota manda para importação manual. A patota envia e acompanha; os
/// administradores da plataforma baixam e mudam a situação. A autorização de cada lado é
/// do controller — o serviço confia em quem chama.
/// </summary>
public interface IGroupSpreadsheetService
{
    Task<Result<GroupSpreadsheetDto>> UploadAsync(
        Guid groupId, Guid uploadedByUserId, GroupSpreadsheetKind kind,
        string fileName, long length, Stream content, string? note, CancellationToken ct);

    Task<Result<List<GroupSpreadsheetDto>>> GetForGroupAsync(Guid groupId, CancellationToken ct);

    /// <summary>A patota só desiste do envio enquanto ninguém começou a importar.</summary>
    Task<Result> DeleteForGroupAsync(Guid groupId, Guid spreadsheetId, CancellationToken ct);

    Task<Result<List<GroupSpreadsheetDto>>> GetAllAsync(
        GroupSpreadsheetStatus? status, GroupSpreadsheetKind? kind, Guid? groupId, CancellationToken ct);

    Task<Result<GroupSpreadsheetDownload>> DownloadAsync(Guid spreadsheetId, CancellationToken ct);

    Task<Result<GroupSpreadsheetDto>> ChangeStatusAsync(
        Guid spreadsheetId, ChangeGroupSpreadsheetStatusDto dto, Guid changedByUserId, CancellationToken ct);

    Task<Result> DeleteAsync(Guid spreadsheetId, CancellationToken ct);
}
