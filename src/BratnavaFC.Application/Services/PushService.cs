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

            var existing = await _context.PushTokens
                .FirstOrDefaultAsync(t => t.Token == token, cancellationToken);

            if (existing is not null)
            {
                // Token já existe — reatribuir ao usuário atual e ativar
                existing.ReassignToUser(userId);
            }
            else
            {
                // Novo token para este usuário
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
        CancellationToken cancellationToken = default)
    {
        var tokens = await _context.PushTokens
            .Where(t => t.UserId == userId && t.IsActive)
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "[Push] SendToUser userId={UserId} | tokens={Count} | title={Title}",
            userId, tokens.Count, title);

        if (tokens.Count == 0)
        {
            _logger.LogWarning("[Push] Nenhum token ativo para usuário {UserId}. Notificação ignorada.", userId);
            return;
        }

        await SendToTokensAsync(tokens, title, body, data, cancellationToken);
    }

    public async Task SendToGroupAsync(
        Guid groupId, string title, string body,
        Dictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        // Busca userId de todos os jogadores ativos do grupo que têm tokens
        var userIds = await _context.Players
            .Where(p => p.GroupId == groupId && p.UserId != null)
            .Select(p => p.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (userIds.Count == 0) return;

        var tokens = await _context.PushTokens
            .Where(t => userIds.Contains(t.UserId) && t.IsActive)
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0) return;

        await SendToTokensAsync(tokens, title, body, data, cancellationToken);
    }

    public async Task SendToTokensAsync(
        IEnumerable<string> tokens, string title, string body,
        Dictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        var tokenList = tokens.Distinct().ToList();
        if (tokenList.Count == 0) return;

        // Firebase aceita no máximo 500 tokens por lote
        const int batchSize = 500;
        var batches = tokenList
            .Select((t, i) => (t, i))
            .GroupBy(x => x.i / batchSize)
            .Select(g => g.Select(x => x.t).ToList());

        foreach (var batch in batches)
        {
            await SendBatchAsync(batch, title, body, data, cancellationToken);
        }
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

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
