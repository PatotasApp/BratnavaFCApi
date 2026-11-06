using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Spreadsheets;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Spreadsheets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class GroupSpreadsheetService : IGroupSpreadsheetService
{
    public const string NotificationType = "group_spreadsheet";

    private const int SignatureBytes = 8 * 1024;

    private readonly AppDbContext _db;
    private readonly ISpreadsheetStorage _storage;
    private readonly IPushService _push;
    private readonly ILogger<GroupSpreadsheetService> _logger;

    public GroupSpreadsheetService(
        AppDbContext db,
        ISpreadsheetStorage storage,
        IPushService push,
        ILogger<GroupSpreadsheetService> logger)
    {
        _db = db;
        _storage = storage;
        _push = push;
        _logger = logger;
    }

    public async Task<Result<GroupSpreadsheetDto>> UploadAsync(
        Guid groupId, Guid uploadedByUserId, GroupSpreadsheetKind kind,
        string fileName, long length, Stream content, string? note, CancellationToken ct)
    {
        if (!Enum.IsDefined(kind))
            return Result<GroupSpreadsheetDto>.Fail("Informe se a planilha é de jogos ou de pagamentos.");
        if (length <= 0)
            return Result<GroupSpreadsheetDto>.Fail("Selecione um arquivo.");
        if (length > SpreadsheetFileRules.MaxSizeBytes)
            return Result<GroupSpreadsheetDto>.Fail("A planilha deve ter no máximo 10 MB.");

        var safeName = SpreadsheetFileRules.SanitizeFileName(fileName);
        var extension = Path.GetExtension(safeName).ToLowerInvariant();
        var contentType = SpreadsheetFileRules.ContentTypeOf(extension);
        if (contentType is null)
            return Result<GroupSpreadsheetDto>.Fail("Envie a planilha em .xlsx, .xls, .csv, .ods ou .pdf.");

        if (!await _db.Groups.AnyAsync(g => g.Id == groupId, ct))
            return Result<GroupSpreadsheetDto>.Fail("Patota não encontrada.", ResultStatus.NotFound);

        var pending = await _db.GroupSpreadsheets
            .CountAsync(s => s.GroupId == groupId && s.Status == GroupSpreadsheetStatus.Received, ct);
        if (pending >= SpreadsheetFileRules.MaxPendingPerGroup)
            return Result<GroupSpreadsheetDto>.Fail(
                $"A patota já tem {pending} planilhas aguardando análise. Espere a equipe começar a importar.",
                ResultStatus.Conflict);

        // Até 10 MB: cabe na memória, e o R2 precisa do Content-Length de qualquer jeito.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length == 0)
            return Result<GroupSpreadsheetDto>.Fail("Selecione um arquivo.");
        if (buffer.Length > SpreadsheetFileRules.MaxSizeBytes)
            return Result<GroupSpreadsheetDto>.Fail("A planilha deve ter no máximo 10 MB.");

        var head = new ArraySegment<byte>(buffer.GetBuffer(), 0, (int)Math.Min(buffer.Length, SignatureBytes));
        if (!SpreadsheetFileRules.ContentMatchesExtension(extension, head))
            return Result<GroupSpreadsheetDto>.Fail(
                $"O conteúdo do arquivo não parece um {extension}. Exporte a planilha de novo e tente outra vez.");

        var objectKey = SpreadsheetObjectKeys.New(groupId, extension);
        try
        {
            buffer.Position = 0;
            await _storage.SaveAsync(objectKey, buffer, contentType, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Planilhas] Falha ao gravar a planilha da patota {GroupId} em {Storage}.", groupId, _storage.Description);
            return Result<GroupSpreadsheetDto>.Fail("Não foi possível guardar a planilha agora. Tente novamente.");
        }

        var entity = new GroupSpreadsheetEntity(
            groupId, uploadedByUserId, kind, safeName, objectKey, contentType, buffer.Length, note);
        _db.GroupSpreadsheets.Add(entity);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // Sem a linha ninguém acharia o arquivo: não deixa órfão no storage.
            await DeleteFilesQuietlyAsync([objectKey], CancellationToken.None);
            throw;
        }

        _logger.LogInformation(
            "[Planilhas] Patota {GroupId} enviou planilha {SpreadsheetId} ({Kind}, {Size} bytes).",
            groupId, entity.Id, kind, buffer.Length);

        var dto = (await ProjectAsync(_db.GroupSpreadsheets.Where(s => s.Id == entity.Id), includeEmail: false, ct)).Single();
        return Result<GroupSpreadsheetDto>.Ok(dto, "Planilha enviada. Avisaremos quando a importação começar.", ResultStatus.Created);
    }

    public async Task<Result<List<GroupSpreadsheetDto>>> GetForGroupAsync(Guid groupId, CancellationToken ct)
        => Result<List<GroupSpreadsheetDto>>.Ok(await ProjectAsync(
            _db.GroupSpreadsheets.Where(s => s.GroupId == groupId), includeEmail: false, ct));

    public async Task<Result> DeleteForGroupAsync(Guid groupId, Guid spreadsheetId, CancellationToken ct)
    {
        var entity = await _db.GroupSpreadsheets
            .FirstOrDefaultAsync(s => s.Id == spreadsheetId && s.GroupId == groupId, ct);
        if (entity is null)
            return Result.Fail("Planilha não encontrada.", ResultStatus.NotFound);
        if (entity.Status != GroupSpreadsheetStatus.Received)
            return Result.Fail("A importação desta planilha já começou; ela não pode mais ser removida.", ResultStatus.Conflict);

        return await RemoveAsync(entity, ct);
    }

    public async Task<Result<List<GroupSpreadsheetDto>>> GetAllAsync(
        GroupSpreadsheetStatus? status, GroupSpreadsheetKind? kind, Guid? groupId, CancellationToken ct)
    {
        var query = _db.GroupSpreadsheets.AsQueryable();
        if (status is { } st) query = query.Where(s => s.Status == st);
        if (kind is { } k) query = query.Where(s => s.Kind == k);
        if (groupId is { } g) query = query.Where(s => s.GroupId == g);

        return Result<List<GroupSpreadsheetDto>>.Ok(await ProjectAsync(query, includeEmail: true, ct, take: 500));
    }

    public async Task<Result<GroupSpreadsheetDownload>> DownloadAsync(Guid spreadsheetId, CancellationToken ct)
    {
        var entity = await _db.GroupSpreadsheets.AsNoTracking().FirstOrDefaultAsync(s => s.Id == spreadsheetId, ct);
        if (entity is null)
            return Result<GroupSpreadsheetDownload>.Fail("Planilha não encontrada.", ResultStatus.NotFound);

        try
        {
            var stream = await _storage.OpenReadAsync(entity.StorageKey, ct);
            return Result<GroupSpreadsheetDownload>.Ok(
                new GroupSpreadsheetDownload(stream, entity.ContentType, entity.OriginalFileName));
        }
        catch (FileNotFoundException)
        {
            _logger.LogWarning("[Planilhas] Arquivo da planilha {SpreadsheetId} sumiu do storage ({Key}).", entity.Id, entity.StorageKey);
            return Result<GroupSpreadsheetDownload>.Fail("O arquivo desta planilha não foi encontrado no armazenamento.", ResultStatus.NotFound);
        }
    }

    public async Task<Result<GroupSpreadsheetDto>> ChangeStatusAsync(
        Guid spreadsheetId, ChangeGroupSpreadsheetStatusDto dto, Guid changedByUserId, CancellationToken ct)
    {
        if (dto is null || !Enum.IsDefined(dto.Status))
            return Result<GroupSpreadsheetDto>.Fail("Situação inválida.");

        var entity = await _db.GroupSpreadsheets.FirstOrDefaultAsync(s => s.Id == spreadsheetId, ct);
        if (entity is null)
            return Result<GroupSpreadsheetDto>.Fail("Planilha não encontrada.", ResultStatus.NotFound);

        var changed = entity.ChangeStatus(dto.Status, changedByUserId, dto.AdminNote);
        await _db.SaveChangesAsync(ct);

        if (changed && dto.Status != GroupSpreadsheetStatus.Received)
            await NotifyUploaderAsync(entity, ct);

        var result = (await ProjectAsync(_db.GroupSpreadsheets.Where(s => s.Id == entity.Id), includeEmail: true, ct)).Single();
        return Result<GroupSpreadsheetDto>.Ok(result);
    }

    public async Task<Result> DeleteAsync(Guid spreadsheetId, CancellationToken ct)
    {
        var entity = await _db.GroupSpreadsheets.FirstOrDefaultAsync(s => s.Id == spreadsheetId, ct);
        if (entity is null)
            return Result.Fail("Planilha não encontrada.", ResultStatus.NotFound);

        return await RemoveAsync(entity, ct);
    }

    /// <summary>Texto do aviso: só "Em andamento" e "Finalizada" avisam.</summary>
    public static (string Title, string Body) NotificationText(GroupSpreadsheetEntity s)
    {
        var what = s.Kind == GroupSpreadsheetKind.Matches ? "histórico de jogos" : "pagamentos";
        var (title, body) = s.Status switch
        {
            GroupSpreadsheetStatus.InProgress => (
                "Importação em andamento",
                $"Começamos a importar a sua planilha de {what} ({s.OriginalFileName})."),
            _ => (
                "Planilha importada",
                $"Os dados da sua planilha de {what} ({s.OriginalFileName}) já estão no app."),
        };
        if (!string.IsNullOrWhiteSpace(s.AdminNote))
            body += $" Observação: {s.AdminNote}";
        return (title, body);
    }

    private async Task NotifyUploaderAsync(GroupSpreadsheetEntity entity, CancellationToken ct)
    {
        try
        {
            var (title, body) = NotificationText(entity);
            await _push.SendToUserAsync(
                entity.UploadedByUserId, title, body,
                new Dictionary<string, string>
                {
                    ["type"] = NotificationType,
                    ["groupId"] = entity.GroupId.ToString(),
                    ["spreadsheetId"] = entity.Id.ToString(),
                    ["status"] = entity.Status.ToString(),
                },
                ct,
                entity.GroupId);
        }
        catch (Exception ex)
        {
            // A situação já foi gravada; falha no aviso não desfaz a troca no painel.
            _logger.LogError(ex, "[Planilhas] Falha ao avisar sobre a planilha {SpreadsheetId}.", entity.Id);
        }
    }

    private async Task<Result> RemoveAsync(GroupSpreadsheetEntity entity, CancellationToken ct)
    {
        var key = entity.StorageKey;
        _db.GroupSpreadsheets.Remove(entity);
        await _db.SaveChangesAsync(ct);
        await DeleteFilesQuietlyAsync([key], ct);
        return Result.Ok("Planilha removida.");
    }

    private Task DeleteFilesQuietlyAsync(IReadOnlyCollection<string> keys, CancellationToken ct)
        => DeleteFilesQuietlyAsync(_storage, _logger, keys, ct);

    /// <summary>
    /// Best-effort, depois do commit: arquivo órfão custa espaço; falhar a operação que a
    /// pessoa pediu (excluir patota, excluir conta) por causa do storage custa muito mais.
    /// </summary>
    public static async Task DeleteFilesQuietlyAsync(
        ISpreadsheetStorage? storage, ILogger logger, IReadOnlyCollection<string>? keys, CancellationToken ct)
    {
        if (storage is null || keys is null || keys.Count == 0) return;
        foreach (var key in keys)
        {
            try
            {
                await storage.DeleteAsync(key, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Planilhas] Arquivo {Key} não pôde ser removido de {Storage}.", key, storage.Description);
            }
        }
    }

    private async Task<List<GroupSpreadsheetDto>> ProjectAsync(
        IQueryable<GroupSpreadsheetEntity> query, bool includeEmail, CancellationToken ct, int take = 200)
    {
        var rows = await query.AsNoTracking()
            .OrderByDescending(s => s.CreateDate)
            .Take(take)
            .ToListAsync(ct);

        var groupIds = rows.Select(r => r.GroupId).Distinct().ToList();
        var userIds = rows.Select(r => r.UploadedByUserId).Distinct().ToList();

        var groups = await _db.Groups.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToDictionaryAsync(u => u.Id, ct);

        return rows.Select(r =>
        {
            var user = users.GetValueOrDefault(r.UploadedByUserId);
            return new GroupSpreadsheetDto(
                r.Id, r.GroupId, groups.GetValueOrDefault(r.GroupId) ?? "",
                r.Kind, r.OriginalFileName, r.SizeBytes, r.Note,
                r.Status, r.AdminNote,
                r.UploadedByUserId,
                user is null ? "Usuário deletado" : $"{user.FirstName} {user.LastName}".Trim(),
                includeEmail ? user?.Email : null,
                r.CreateDate, r.StatusChangedAt);
        }).ToList();
    }
}
