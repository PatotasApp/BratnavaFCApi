using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public class PushService : IPushService
{
    private readonly AppDbContext _context;
    private readonly ILogger<PushService> _logger;

    public PushService(AppDbContext context, ILogger<PushService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ── Registro de token ─────────────────────────────────────────────────────

    public async Task<Result> RegisterTokenAsync(
        Guid userId, string token, string platform, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
                return Result.Fail("Token é obrigatório.");

            platform = platform.ToLowerInvariant().Trim();
            if (platform != "android" && platform != "ios")
                return Result.Fail("Platform deve ser 'android' ou 'ios'.");

            // Lookup por (userId + token): não roubamos o token de outro usuário.
            // O mesmo dispositivo físico pode ter linhas para diferentes usuários.
            var existing = await _context.PushTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Token == token, cancellationToken);

            if (existing is not null)
            {
                // Linha já existe para este usuário — apenas reativar
                existing.Reactivate();
            }
            else
            {
                // Primeiro registro deste par (usuário, token)
                var newToken = new PushTokenEntity(userId, token, platform);
                _context.PushTokens.Add(newToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            return Result.Ok("Token registrado com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao registrar push token para usuário {UserId}.", userId);
            throw;
        }
    }

    // ── Envio de notificações ─────────────────────────────────────────────────

    public async Task SendToUserAsync(
        Guid userId, string title, string body,
        Dictionary<string, string>? data = null,
        CancellationToken cancellationToken = default,
        Guid? groupId = null)
    {
        var tokens = await _context.PushTokens
            .Where(t => t.UserId == userId && t.IsActive)
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "[Push] SendToUser userId={UserId} | tokens={Count} | title={Title}",
            userId, tokens.Count, title);

        var prefixedTitle = groupId.HasValue
            ? await PrefixWithGroupNameAsync(title, groupId.Value, cancellationToken)
            : title;

        // Identifica o usuário no título para dispositivos com múltiplas contas
        var personalizedTitle = await PersonalizeTitleAsync(userId, prefixedTitle, cancellationToken);

        // Injeta userId no payload para que o app saiba a qual conta pertence
        var enrichedData = EnrichWithUserId(data, userId);

        // Persiste na caixa de entrada independente de ter token ativo
        await PersistAsync([userId], groupId, personalizedTitle, body, enrichedData, cancellationToken);

        if (tokens.Count == 0)
        {
            _logger.LogWarning("[Push] Nenhum token ativo para usuário {UserId}. Push ignorado, notificação salva.", userId);
            return;
        }

        await SendToTokensAsync(tokens, personalizedTitle, body, enrichedData, cancellationToken);
    }

    public async Task SendDataOnlyToGroupAsync(
        Guid groupId, Dictionary<string, string> data,
        CancellationToken cancellationToken = default)
    {
        // Prefixa o title dentro do data dict com o nome do grupo
        if (data.TryGetValue("title", out var rawTitle))
        {
            var groupName = await GetGroupNameAsync(groupId, cancellationToken);
            data["title"] = $"{groupName} · {rawTitle}";
        }

        var userIds = await _context.Players
            .Where(p => p.GroupId == groupId && p.UserId != null)
            .Select(p => p.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Persiste no sininho usando title/body do payload data
        data.TryGetValue("title", out var inboxTitle);
        data.TryGetValue("body",  out var inboxBody);
        data.TryGetValue("type",  out var inboxType);
        if (!string.IsNullOrEmpty(inboxTitle))
            await PersistAsync(userIds, groupId, inboxTitle, inboxBody ?? string.Empty, data, cancellationToken, inboxType);

        if (userIds.Count == 0) return;

        var tokens = await _context.PushTokens
            .Where(t => userIds.Contains(t.UserId) && t.IsActive)
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0) return;

        // Envia sem campo Notification — o Flutter exibe a notificação local com botões
        var tokenList = tokens.Distinct().ToList();
        const int batchSize = 500;
        var batches = tokenList
            .Select((t, i) => (t, i))
            .GroupBy(x => x.i / batchSize)
            .Select(g => g.Select(x => x.t).ToList());

        foreach (var batch in batches)
        {
            var message = new MulticastMessage
            {
                Tokens      = batch,
                Notification = null,   // data-only: sem banner automático do sistema
                Data        = data,
                Android     = new AndroidConfig { Priority = Priority.High },
                Apns        = new ApnsConfig
                {
                    Aps = new Aps { ContentAvailable = true }, // acorda o handler no iOS
                },
            };

            try
            {
                var response = await FirebaseMessaging.DefaultInstance
                    .SendEachForMulticastAsync(message, cancellationToken);
                _logger.LogInformation(
                    "[Push DataOnly] FCM batch: {S} enviados, {F} falhas.",
                    response.SuccessCount, response.FailureCount);

                var invalid = new List<string>();
                for (int i = 0; i < response.Responses.Count; i++)
                {
                    if (response.Responses[i].IsSuccess) continue;
                    var code = response.Responses[i].Exception?.MessagingErrorCode;
                    if (code is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
                        invalid.Add(batch[i]);
                }
                if (invalid.Count > 0)
                    await DeactivateTokensAsync(invalid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao enviar batch data-only via FCM.");
            }
        }
    }

    public async Task SendToGroupAsync(
        Guid groupId, string title, string body,
        Dictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        var groupName = await GetGroupNameAsync(groupId, cancellationToken);
        var userIds = await _context.Players
            .Where(p => p.GroupId == groupId && p.UserId != null)
            .Select(p => p.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        await SendToUserListAsync(userIds, $"{groupName} · {title}", body, data, cancellationToken, groupId);
    }

    public async Task SendToGroupAdminsAsync(
        Guid groupId, string title, string body,
        Dictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        var groupName = await GetGroupNameAsync(groupId, cancellationToken);
        var userIds = await _context.GroupAdmins
            .Where(ga => ga.GroupId == groupId)
            .Select(ga => ga.UserId)
            .ToListAsync(cancellationToken);

        await SendToUserListAsync(userIds, $"{groupName} · {title}", body, data, cancellationToken, groupId);
    }

    public async Task SendToGroupFinanceirosAsync(
        Guid groupId, string title, string body,
        Dictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        var groupName = await GetGroupNameAsync(groupId, cancellationToken);
        var userIds = await _context.GroupFinanceiros
            .Where(gf => gf.GroupId == groupId)
            .Select(gf => gf.UserId)
            .ToListAsync(cancellationToken);

        await SendToUserListAsync(userIds, $"{groupName} · {title}", body, data, cancellationToken, groupId);
    }

    public Task SendToUsersAsync(
        List<Guid> userIds, string title, string body,
        Dictionary<string, string>? data = null,
        CancellationToken cancellationToken = default,
        Guid? groupId = null)
        => SendToUserListAsync(userIds, title, body, data, cancellationToken, groupId);

    private async Task SendToUserListAsync(
        List<Guid> userIds, string title, string body,
        Dictionary<string, string>? data,
        CancellationToken cancellationToken,
        Guid? groupId = null)
    {
        if (userIds.Count == 0) return;

        // Persiste na caixa de entrada de cada usuário (sem prefixo de nome)
        await PersistAsync(userIds, groupId, title, body, data, cancellationToken);

        // Busca tokens agrupados por usuário para poder personalizar o título
        var tokensByUser = await _context.PushTokens
            .Where(t => userIds.Contains(t.UserId) && t.IsActive)
            .Select(t => new { t.UserId, t.Token })
            .ToListAsync(cancellationToken);

        if (tokensByUser.Count == 0) return;

        // Busca nomes de usuário para personalização
        var ids = tokensByUser.Select(t => t.UserId).Distinct().ToList();
        var names = await _context.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.UserName ?? (u.FirstName + " " + u.LastName).Trim() })
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        // Agrupa por usuário e envia com título personalizado
        foreach (var group in tokensByUser.GroupBy(t => t.UserId))
        {
            var tokens = group.Select(t => t.Token).ToList();
            var userName = names.GetValueOrDefault(group.Key, string.Empty);
            var personalizedTitle = string.IsNullOrEmpty(userName) ? title : $"[{userName}] {title}";
            var enrichedData = EnrichWithUserId(data, group.Key);
            await SendToTokensAsync(tokens, personalizedTitle, body, enrichedData, cancellationToken);
        }
    }

    public async Task SendToTokensAsync(
        IEnumerable<string> tokens, string title, string body,
        Dictionary<string, string>? data = null,
        CancellationToken cancellationToken = default,
        Guid? groupId = null)
    {
        var tokenList = tokens.Distinct().ToList();
        if (tokenList.Count == 0) return;

        var prefixedTitle = groupId.HasValue
            ? await PrefixWithGroupNameAsync(title, groupId.Value, cancellationToken)
            : title;

        // Firebase aceita no máximo 500 tokens por lote
        const int batchSize = 500;
        var batches = tokenList
            .Select((t, i) => (t, i))
            .GroupBy(x => x.i / batchSize)
            .Select(g => g.Select(x => x.t).ToList());

        foreach (var batch in batches)
        {
            await SendBatchAsync(batch, prefixedTitle, body, data, cancellationToken);
        }
    }

    // ── Persistência no sininho ───────────────────────────────────────────────

    private async Task PersistAsync(
        List<Guid> userIds, Guid? groupId,
        string title, string body,
        Dictionary<string, string>? data,
        CancellationToken ct,
        string? typeOverride = null)
    {
        if (userIds.Count == 0) return;

        var type     = typeOverride ?? data?.GetValueOrDefault("type");
        var dataJson = data is { Count: > 0 }
            ? System.Text.Json.JsonSerializer.Serialize(data)
            : null;

        foreach (var uid in userIds)
        {
            _context.UserNotifications.Add(
                new UserNotificationEntity(uid, groupId, title, body, type, dataJson));
        }

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Push] Falha ao persistir notificações no inbox para {Count} usuário(s).", userIds.Count);
        }
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    /// <summary>
    /// Prefixa o título com [username] para que dispositivos com múltiplas
    /// contas saibam a qual usuário a notificação pertence.
    /// Ex.: "[luis] Senha alterada"
    /// </summary>
    private async Task<string> PersonalizeTitleAsync(Guid userId, string title, CancellationToken ct)
    {
        var name = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => u.UserName ?? (u.FirstName + " " + u.LastName).Trim())
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(name) ? title : $"[{name}] {title}";
    }

    /// <summary>
    /// Injeta userId no payload de dados — permite ao app identificar a conta
    /// destinatária, especialmente em foreground com múltiplas contas ativas.
    /// </summary>
    private static Dictionary<string, string> EnrichWithUserId(
        Dictionary<string, string>? data, Guid userId)
    {
        var enriched = data is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(data);
        enriched["userId"] = userId.ToString();
        return enriched;
    }

    /// <summary>Retorna o nome do grupo ou string vazia se não encontrado.</summary>
    private async Task<string> GetGroupNameAsync(Guid groupId, CancellationToken ct)
    {
        return await _context.Groups
            .Where(g => g.Id == groupId)
            .Select(g => g.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;
    }

    private async Task<string> PrefixWithGroupNameAsync(string title, Guid groupId, CancellationToken ct)
    {
        var name = await GetGroupNameAsync(groupId, ct);
        return string.IsNullOrEmpty(name) ? title : $"{name} · {title}";
    }

    private async Task SendBatchAsync(
        List<string> tokens, string title, string body,
        Dictionary<string, string>? data,
        CancellationToken cancellationToken)
    {
        var message = new MulticastMessage
        {
            Tokens = tokens,
            Notification = new Notification
            {
                Title = title,
                Body = body,
            },
            Data = data ?? new Dictionary<string, string>(),
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                Notification = new AndroidNotification
                {
                    Sound = "default",
                    ClickAction = "FLUTTER_NOTIFICATION_CLICK",
                },
            },
            Apns = new ApnsConfig
            {
                Aps = new Aps
                {
                    Sound = "default",
                    Badge = 1,
                },
            },
        };

        BatchResponse response;
        try
        {
            response = await FirebaseMessaging.DefaultInstance
                .SendEachForMulticastAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enviar batch de {Count} notificações via FCM.", tokens.Count);
            return;
        }

        _logger.LogInformation(
            "FCM batch: {Success} enviados, {Failure} falhas (total {Total}).",
            response.SuccessCount, response.FailureCount, tokens.Count);

        // Desativar tokens inválidos
        var invalidTokens = new List<string>();
        for (int i = 0; i < response.Responses.Count; i++)
        {
            if (response.Responses[i].IsSuccess) continue;

            var errorCode = response.Responses[i].Exception?.MessagingErrorCode;
            if (errorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
            {
                invalidTokens.Add(tokens[i]);
                _logger.LogWarning("Token inválido/expirado desativado: {Token}", tokens[i]);
            }
        }

        if (invalidTokens.Count > 0)
        {
            await DeactivateTokensAsync(invalidTokens, cancellationToken);
        }
    }

    private async Task DeactivateTokensAsync(
        List<string> tokenValues, CancellationToken cancellationToken)
    {
        var entities = await _context.PushTokens
            .Where(t => tokenValues.Contains(t.Token))
            .ToListAsync(cancellationToken);

        foreach (var entity in entities)
            entity.Deactivate();

        await _context.SaveChangesAsync(cancellationToken);
    }
}
